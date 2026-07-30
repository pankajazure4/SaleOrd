using SaleOrd.SyncAgent.Models;

namespace SaleOrd.SyncAgent.Services;

// Mirrors the web app's Services/VoucherInventorySyncService.cs — one-time
// full historical batch sync (chunked by date, from the company's Tally
// "books beginning from" date), then incremental syncs keyed on Tally's
// AlterId watermark (Company.LastVoucherAlterId) from then on. See the web
// app's version for the fuller design notes on why this replaced the old
// bounded-rescan "last sale rate" approach.
public class VoucherInventorySyncService
{
    // 1-day batches confirmed the timeouts were caused by the "Bad formula"
    // rejection, not date-range size — Tally errored out instantly on every
    // batch regardless of window. Now that the formula is fixed, back up to
    // a wider window to cut down the number of round trips during backfill.
    private const int BatchDays = 7;

    private readonly SqlDataService _sql;
    private readonly TallyService _tally;
    private readonly AgentLogger _logger;

    public VoucherInventorySyncService(SqlDataService sql, TallyService tally, AgentLogger logger)
    {
        _sql = sql;
        _tally = tally;
        _logger = logger;
    }

    public async Task<(int Added, int Updated, string Mode)> SyncCompanyAsync(
        string connString, string tallyUrl, Company company)
    {
        var salesTypes = await _tally.GetSalesVoucherTypeNamesAsync(tallyUrl, company.TallyCompanyName);
        _logger.Info($"[{company.CompanyName}] Sales voucher types matched (Abbreviation=Sale): " +
            (salesTypes.Count > 0 ? string.Join(", ", salesTypes) : "(none — falling back to \"Sales\")"));

        int totalAdded = 0, totalUpdated = 0;
        long maxAlterIdSeen = company.LastVoucherAlterId ?? 0;

        if (company.LastVoucherAlterId == null)
        {
            var startDate = await _tally.GetCompanyStartDateAsync(tallyUrl, company.TallyCompanyName)
                ?? DateTime.Today.AddYears(-1);
            var endDate = DateTime.Today;

            _logger.Info($"[{company.CompanyName}] Voucher inventory: full historical backfill from {startDate:dd-MMM-yyyy} to {endDate:dd-MMM-yyyy}, {BatchDays}-day batches...");

            for (var batchStart = startDate.Date; batchStart <= endDate; batchStart = batchStart.AddDays(BatchDays))
            {
                var batchEnd = batchStart.AddDays(BatchDays - 1);
                if (batchEnd > endDate) batchEnd = endDate;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _logger.Info($"[{company.CompanyName}] Fetching batch {batchStart:dd-MMM-yyyy} to {batchEnd:dd-MMM-yyyy}...");
                var (batch, ok) = await _tally.GetVoucherInventoryAsync(
                    tallyUrl, company.TallyCompanyName, salesTypes, batchStart, batchEnd, null);
                sw.Stop();

                if (!ok)
                {
                    // A transport failure (not "zero vouchers this batch") must
                    // not advance the watermark or be mistaken for a completed
                    // backfill — bail out now so LastVoucherAlterId stays null
                    // and the next cycle safely retries the whole thing.
                    _logger.Info($"[{company.CompanyName}] Batch {batchStart:dd-MMM-yyyy} to {batchEnd:dd-MMM-yyyy} failed — stopping backfill early, will retry next cycle.");
                    return (totalAdded, totalUpdated, "Full (incomplete — will retry)");
                }

                _logger.Info($"[{company.CompanyName}] Batch {batchStart:dd-MMM-yyyy} to {batchEnd:dd-MMM-yyyy}: {batch.Count} row(s) in {sw.ElapsedMilliseconds}ms.");

                if (batch.Count > 0)
                {
                    var (added, updated) = await _sql.UpsertVoucherInventoryAsync(connString, company.CompanyId, batch);
                    totalAdded += added;
                    totalUpdated += updated;
                    maxAlterIdSeen = Math.Max(maxAlterIdSeen, batch.Max(r => r.AlterId));
                }

                if (batchStart < endDate)
                    await Task.Delay(600);
            }

            // Only mark the backfill complete once every chunk has been
            // scanned — an interruption partway leaves LastVoucherAlterId
            // null, so the next run safely redoes the full backfill instead
            // of switching to incremental mode having missed a date range.
            await _sql.UpdateLastVoucherAlterIdAsync(connString, company.CompanyId, maxAlterIdSeen);
            company.LastVoucherAlterId = maxAlterIdSeen;

            return (totalAdded, totalUpdated, "Full");
        }
        else
        {
            var (records, ok) = await _tally.GetVoucherInventoryAsync(
                tallyUrl, company.TallyCompanyName, salesTypes, null, null, company.LastVoucherAlterId);

            if (!ok)
            {
                _logger.Info($"[{company.CompanyName}] Incremental voucher sync failed — will retry next cycle.");
                return (0, 0, "Incremental (failed)");
            }

            if (records.Count > 0)
            {
                var (added, updated) = await _sql.UpsertVoucherInventoryAsync(connString, company.CompanyId, records);
                totalAdded += added;
                totalUpdated += updated;
                maxAlterIdSeen = Math.Max(maxAlterIdSeen, records.Max(r => r.AlterId));
                await _sql.UpdateLastVoucherAlterIdAsync(connString, company.CompanyId, maxAlterIdSeen);
                company.LastVoucherAlterId = maxAlterIdSeen;
            }

            return (totalAdded, totalUpdated, "Incremental");
        }
    }
}
