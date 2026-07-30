using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;

namespace SaleOrd.Services;

// Syncs Sales-voucher line-inventory data (VoucherInventoryEntry) for one
// company: a one-time full historical batch pass (chunked by date, from the
// company's Tally "books beginning from" date), then incremental syncs keyed
// on Tally's AlterId watermark (Company.LastVoucherAlterId) from then on —
// the same two-phase pattern SyncMast uses for its own voucher sync, chosen
// after the previous 180-day-rescan-every-cycle approach proved unreliable
// (crashed Tally once, hung for 30+ minutes on a later run) on a client
// install with a large, high-volume company.
//
// This is a genuine exception to this codebase's usual convention of
// duplicating sync logic between MasterSyncJob and SyncController — that
// convention fits small, simple upserts; the batching/watermark logic here
// is complex enough that duplicating it three ways (background job, manual
// sync, agent) would be a real bug-multiplication risk. MasterSyncJob and
// SyncController both call this single shared implementation; the agent
// (which can't reference this project) has its own mirrored copy.
public class VoucherInventorySyncService
{
    // 1-day batches confirmed the timeouts were caused by the "Bad formula"
    // rejection, not date-range size — Tally errored out instantly on every
    // batch regardless of window. Now that the formula is fixed, back up to
    // a wider window to cut down the number of round trips during backfill.
    private const int BatchDays = 7;

    private readonly ILogger<VoucherInventorySyncService> _logger;

    public VoucherInventorySyncService(ILogger<VoucherInventorySyncService> logger)
    {
        _logger = logger;
    }

    public async Task<(int Added, int Updated, string Mode)> SyncCompanyAsync(
        AppDbContext db, TallyService tallyService, string tallyUrl, Company company)
    {
        var salesTypes = await tallyService.GetSalesVoucherTypeNamesAsync(tallyUrl, company.TallyCompanyName);
        _logger.LogInformation("[{Company}] Sales voucher types matched (Abbreviation=Sale): {Types}",
            company.TallyCompanyName, salesTypes.Count > 0 ? string.Join(", ", salesTypes) : "(none — falling back to \"Sales\")");

        int totalAdded = 0, totalUpdated = 0;
        long maxAlterIdSeen = company.LastVoucherAlterId ?? 0;

        if (company.LastVoucherAlterId == null)
        {
            // Full historical backfill — chunked so no single Tally request
            // has to return years of data at once.
            var startDate = await tallyService.GetCompanyStartDateAsync(tallyUrl, company.TallyCompanyName)
                ?? DateTime.Today.AddYears(-1);
            var endDate = DateTime.Today;

            _logger.LogInformation("[{Company}] Voucher backfill: {From} to {To}, {Days}-day batches.",
                company.TallyCompanyName, startDate, endDate, BatchDays);

            for (var batchStart = startDate.Date; batchStart <= endDate; batchStart = batchStart.AddDays(BatchDays))
            {
                var batchEnd = batchStart.AddDays(BatchDays - 1);
                if (batchEnd > endDate) batchEnd = endDate;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _logger.LogInformation("[{Company}] Fetching batch {Start:dd-MMM-yyyy} to {End:dd-MMM-yyyy}...",
                    company.TallyCompanyName, batchStart, batchEnd);
                var (batch, ok) = await tallyService.GetVoucherInventoryAsync(
                    tallyUrl, company.TallyCompanyName, salesTypes, batchStart, batchEnd, null);
                sw.Stop();

                if (!ok)
                {
                    // A transport failure (not "zero vouchers this batch")
                    // must not advance the watermark or be mistaken for a
                    // completed backfill — bail out now so LastVoucherAlterId
                    // stays null and the next cycle safely retries the whole
                    // thing rather than silently treating the rest of the
                    // date range as scanned.
                    _logger.LogWarning("[{Company}] Batch {Start:dd-MMM-yyyy} to {End:dd-MMM-yyyy} failed — stopping backfill early, will retry next cycle.",
                        company.TallyCompanyName, batchStart, batchEnd);
                    return (totalAdded, totalUpdated, "Full (incomplete — will retry)");
                }

                _logger.LogInformation("[{Company}] Batch {Start:dd-MMM-yyyy} to {End:dd-MMM-yyyy}: {Rows} row(s) in {Ms}ms.",
                    company.TallyCompanyName, batchStart, batchEnd, batch.Count, sw.ElapsedMilliseconds);

                if (batch.Count > 0)
                {
                    var (added, updated) = await UpsertBatchAsync(db, company.CompanyId, batch);
                    totalAdded += added;
                    totalUpdated += updated;
                    maxAlterIdSeen = Math.Max(maxAlterIdSeen, batch.Max(r => r.AlterId));
                }

                if (batchStart < endDate)
                    await Task.Delay(600);
            }

            // Only mark the backfill complete once every chunk has been
            // scanned — if this loop gets interrupted partway (app restart,
            // exception), LastVoucherAlterId stays null and the next run
            // safely redoes the full backfill rather than switching to
            // incremental mode having missed some date range.
            company.LastVoucherAlterId = maxAlterIdSeen;
            await db.SaveChangesAsync();

            return (totalAdded, totalUpdated, "Full");
        }
        else
        {
            var (records, ok) = await tallyService.GetVoucherInventoryAsync(
                tallyUrl, company.TallyCompanyName, salesTypes, null, null, company.LastVoucherAlterId);

            if (!ok)
            {
                _logger.LogWarning("[{Company}] Incremental voucher sync failed — will retry next cycle.", company.TallyCompanyName);
                return (0, 0, "Incremental (failed)");
            }

            if (records.Count > 0)
            {
                var (added, updated) = await UpsertBatchAsync(db, company.CompanyId, records);
                totalAdded += added;
                totalUpdated += updated;
                maxAlterIdSeen = Math.Max(maxAlterIdSeen, records.Max(r => r.AlterId));
                company.LastVoucherAlterId = maxAlterIdSeen;
                await db.SaveChangesAsync();
            }

            return (totalAdded, totalUpdated, "Incremental");
        }
    }

    private async Task<(int Added, int Updated)> UpsertBatchAsync(
        AppDbContext db, int companyId, List<TallyService.VoucherInventoryRecord> records)
    {
        var ledgerIds = (await db.Ledgers
                .Where(l => l.CompanyId == companyId)
                .Select(l => new { l.LedgerId, l.LedgerName })
                .ToListAsync())
            .GroupBy(l => Normalize(l.LedgerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().LedgerId, StringComparer.OrdinalIgnoreCase);

        var itemIds = (await db.StockItems
                .Where(s => s.CompanyId == companyId)
                .Select(s => new { s.StockItemId, s.ItemName })
                .ToListAsync())
            .GroupBy(s => Normalize(s.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().StockItemId, StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0;
        var now = DateTime.Now;

        foreach (var group in records.GroupBy(r => r.GUID))
        {
            // Replace-by-voucher: a re-synced voucher (altered in Tally, or
            // reprocessed because a historical batch overlapped) gets its
            // old rows dropped and fresh ones inserted, rather than trying
            // to line-match and update individual inventory rows.
            var existingRows = await db.VoucherInventoryEntries
                .Where(v => v.CompanyId == companyId && v.VoucherGUID == group.Key)
                .ToListAsync();

            if (existingRows.Count > 0)
            {
                db.VoucherInventoryEntries.RemoveRange(existingRows);
                updated++;
            }
            else
            {
                added++;
            }

            foreach (var rec in group)
            {
                ledgerIds.TryGetValue(Normalize(rec.PartyLedgerName), out var ledgerId);
                itemIds.TryGetValue(Normalize(rec.StockItemName), out var stockItemId);

                db.VoucherInventoryEntries.Add(new VoucherInventoryEntry
                {
                    CompanyId = companyId,
                    VoucherGUID = rec.GUID,
                    VoucherNumber = rec.VoucherNumber,
                    VoucherTypeName = rec.VoucherTypeName,
                    VoucherDate = rec.VoucherDate,
                    AlterId = rec.AlterId,
                    PartyLedgerName = rec.PartyLedgerName,
                    LedgerId = ledgerId == 0 ? null : ledgerId,
                    StockItemName = rec.StockItemName,
                    StockItemId = stockItemId == 0 ? null : stockItemId,
                    ActualQty = rec.ActualQty,
                    BilledQty = rec.BilledQty,
                    Rate = rec.Rate,
                    Amount = rec.Amount,
                    Discount = rec.Discount,
                    GodownName = rec.GodownName,
                    LastSyncedAt = now
                });
            }
        }

        await db.SaveChangesAsync();
        return (added, updated);
    }

    private static string Normalize(string? v) => (v ?? "").Trim();
}
