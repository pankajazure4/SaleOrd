using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.BackgroundServices;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize, Route("api/sync")]
public class SyncController : Controller
{
    private readonly AppDbContext _db;
    private readonly TallyService _tally;
    private readonly UserManager<AppUser> _userManager;
    private readonly UserActivityService _activity;
    private readonly SyncCoordinator _coordinator;
    private readonly IConfiguration _config;
    private readonly VoucherInventorySyncService _voucherSync;

    public SyncController(AppDbContext db, TallyService tally, UserManager<AppUser> userManager, UserActivityService activity, SyncCoordinator coordinator, IConfiguration config, VoucherInventorySyncService voucherSync)
    {
        _db = db;
        _tally = tally;
        _userManager = userManager;
        _activity = activity;
        _coordinator = coordinator;
        _config = config;
        _voucherSync = voucherSync;
    }

    [HttpPost("trigger-masters")]
    public async Task<IActionResult> TriggerMasterSync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        if (user.Role != AppRoles.Admin) return Forbid();

        if (TallySyncMode.IsAgentManaged(_config))
        {
            return Ok(new
            {
                success = true,
                status = "delegated",
                message = "This app doesn't talk to Tally directly — the SaleOrd Sync Agent handles syncing. Check its Status tab for the latest sync time."
            });
        }

        if (!_coordinator.TryStart())
        {
            return Ok(new
            {
                success = false,
                status = "busy",
                message = "A sync is already in progress. Please wait for it to finish."
            });
        }

        try
        {
            return await RunSyncAsync(user);
        }
        finally
        {
            _coordinator.Finish();
        }
    }

    private async Task<IActionResult> RunSyncAsync(AppUser user)
    {
        await _activity.LogAsync(user.Id, user.FullName, user.Role, ActivityActions.SyncTriggered,
            companyId: user.CompanyId);

        var tallyUrl = await _db.AppSettings
            .Where(s => s.Key == "TallyUrl")
            .Select(s => s.Value)
            .FirstOrDefaultAsync()
            ?? "http://localhost:9000";

        var (reachable, openInTally) = await _tally.CheckStatusAsync(tallyUrl);
        if (!reachable)
        {
            return Ok(new
            {
                success = false,
                status = "offline",
                message = "Tally is not reachable. Check the URL in Settings and make sure Tally is running."
            });
        }

        List<Company> userCompanies;
        if (user.Role == AppRoles.Admin)
        {
            userCompanies = await _db.Companies.Where(c => c.IsActive).ToListAsync();
        }
        else
        {
            var userCompanyIds = await _db.UserCompanies
                .Where(uc => uc.UserId == user.Id)
                .Select(uc => uc.CompanyId)
                .ToListAsync();

            if (!userCompanyIds.Any())
                userCompanyIds = new List<int> { user.CompanyId };

            userCompanies = await _db.Companies
                .Where(c => c.IsActive && userCompanyIds.Contains(c.CompanyId))
                .ToListAsync();
        }

        var toSync = userCompanies
            .Where(c => openInTally.Any(o =>
                string.Equals(NormalizeName(o), NormalizeName(c.TallyCompanyName), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (!toSync.Any())
        {
            var openList = string.Join(", ", openInTally);
            var mappedList = string.Join(", ", userCompanies.Select(c => c.TallyCompanyName));
            return Ok(new
            {
                success = false,
                status = "warning",
                message = "Tally is reachable, but no mapped company is currently open.",
                openCompanies = openList,
                mappedCompanies = mappedList
            });
        }

        try
        {
            int totalLedgers = 0, totalItems = 0, totalGodowns = 0, totalRates = 0;

            for (int ci = 0; ci < toSync.Count; ci++)
            {
                var company = toSync[ci];

                // Paced, not back-to-back — hammering Tally's HTTP gateway
                // with rapid-fire requests (masters + rates + then, further
                // down, every pending order's push) was found to crash Tally
                // itself with a memory access violation on a client install.
                var ledgers = await _tally.GetLedgersAsync(tallyUrl, company);
                await Task.Delay(500);
                var (items, taxSlabRecords) = await _tally.GetStockItemsAsync(tallyUrl, company);
                await Task.Delay(500);
                var godowns = await _tally.GetGodownsAsync(tallyUrl, company);
                await Task.Delay(500);

                await UpsertLedgersAsync(company.CompanyId, ledgers);
                await UpsertItemsAsync(company.CompanyId, items, taxSlabRecords);
                await UpsertGodownsAsync(company.CompanyId, godowns);

                var (vNew, vUpd, vMode) = await _voucherSync.SyncCompanyAsync(_db, _tally, tallyUrl, company);

                company.LastMasterSyncAt = DateTime.Now;
                _db.SyncLogs.Add(new SyncLog
                {
                    CompanyId = company.CompanyId,
                    SyncType = "ManualSync",
                    IsSuccess = true,
                    Message = $"[{company.CompanyName}] Ledgers: {ledgers.Count}, Items: {items.Count}, Godowns: {godowns.Count}, Vouchers +{vNew} ~{vUpd} ({vMode})"
                });

                if (ci < toSync.Count - 1)
                    await Task.Delay(800);

                totalLedgers += await _db.Ledgers.CountAsync(l => l.CompanyId == company.CompanyId);
                totalItems += await _db.StockItems.CountAsync(s => s.CompanyId == company.CompanyId);
                totalGodowns += await _db.Godowns.CountAsync(g => g.CompanyId == company.CompanyId);
                totalRates += await _db.VoucherInventoryEntries.CountAsync(v => v.CompanyId == company.CompanyId);
            }

            await _db.SaveChangesAsync();

            var (ordersPushed, ordersFailed) = await PushPendingOrdersAsync(tallyUrl, toSync);
            var invoicesFound = await CheckInvoiceStatusAsync(toSync);

            var syncedNames = string.Join(", ", toSync.Select(c => c.CompanyName));
            var extra = new List<string>();
            if (ordersPushed > 0) extra.Add($"{ordersPushed} order(s) pushed");
            if (ordersFailed > 0) extra.Add($"{ordersFailed} order push failure(s)");
            if (invoicesFound > 0) extra.Add($"{invoicesFound} invoice(s) found");
            var message = $"Synced {toSync.Count} company(s): {syncedNames}";
            if (extra.Any()) message += " — " + string.Join(", ", extra);

            return Ok(new
            {
                success = true,
                status = "online",
                message,
                syncedAt = DateTime.Now.ToString("dd MMM, h:mm tt"),
                ledgers = totalLedgers,
                items = totalItems,
                godowns = totalGodowns,
                rates = totalRates,
                ordersPushed,
                ordersFailed,
                invoicesFound
            });
        }
        catch (Exception ex)
        {
            foreach (var c in toSync)
            {
                _db.SyncLogs.Add(new SyncLog
                {
                    CompanyId = c.CompanyId,
                    SyncType = "ManualSync",
                    IsSuccess = false,
                    Message = ex.Message
                });
            }

            await _db.SaveChangesAsync();
            return Ok(new { success = false, status = "offline", message = "Sync error: " + ex.Message });
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var company = await _db.Companies.FindAsync(user.CompanyId);
        if (company == null) return NotFound();

        var pendingCount = await _db.SaleOrders
            .CountAsync(o => o.CompanyId == user.CompanyId && o.Status == OrderStatus.Pending);

        var errorCount = await _db.SaleOrders
            .CountAsync(o => o.CompanyId == user.CompanyId && o.Status == OrderStatus.Error);

        return Ok(new
        {
            lastSync = company.LastMasterSyncAt?.ToString("dd MMM, h:mm tt") ?? "Never",
            pendingCount,
            errorCount
        });
    }

    [HttpGet("tally-status")]
    public async Task<IActionResult> TallyStatus()
    {
        var tallyUrl = await _db.AppSettings
            .Where(s => s.Key == "TallyUrl")
            .Select(s => s.Value)
            .FirstOrDefaultAsync()
            ?? "http://localhost:9000";

        var (reachable, openNames) = await _tally.CheckStatusAsync(tallyUrl);

        string openCompany = "";
        if (reachable && openNames.Any())
        {
            var match = await _db.Companies
                .Where(c => c.IsActive && openNames.Contains(c.TallyCompanyName))
                .Select(c => c.CompanyName)
                .FirstOrDefaultAsync();
            openCompany = match ?? openNames.First();
        }

        return Ok(new
        {
            reachable,
            status     = reachable ? (openNames.Any() ? "online" : "warning") : "offline",
            openNames,
            openCompany
        });
    }

    private async Task UpsertLedgersAsync(int companyId, List<Ledger> ledgers)
    {
        var incoming = DeduplicateByName(ledgers, l => l.LedgerName, l => l.LedgerName = NormalizeName(l.LedgerName))
            .Select(l =>
            {
                l.CompanyId = companyId;
                return l;
            })
            .ToList();

        var existing = (await _db.Ledgers
                .Where(l => l.CompanyId == companyId)
                .ToListAsync())
            .GroupBy(l => NormalizeName(l.LedgerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var l in incoming)
        {
            if (existing.TryGetValue(l.LedgerName, out var ex))
            {
                ex.Parent = l.Parent;
                ex.Address = l.Address;
                ex.State = l.State;
                ex.MobileNo = l.MobileNo;
                ex.Email = l.Email;
                ex.LedgerFax = l.LedgerFax;
                ex.GSTNo = l.GSTNo;
                ex.TaxType = l.TaxType;
                ex.IncomeTaxNo = l.IncomeTaxNo;
                ex.VATTINNo = l.VATTINNo;
                ex.CreditLimit = l.CreditLimit;
                ex.CreditPeriod = l.CreditPeriod;
                ex.OpeningBalance = l.OpeningBalance;
                ex.ClosingBalance = l.ClosingBalance;
                ex.GUID = l.GUID;
                ex.AlterId = l.AlterId;
                ex.LastSyncedAt = l.LastSyncedAt;
            }
            else
            {
                _db.Ledgers.Add(l);
                existing[l.LedgerName] = l;
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task UpsertItemsAsync(int companyId, List<StockItem> items, List<TallyService.StockItemTaxSlabRecord> taxSlabRecords)
    {
        var incoming = DeduplicateByName(items, s => s.ItemName, s => s.ItemName = NormalizeName(s.ItemName))
            .Select(s =>
            {
                s.CompanyId = companyId;
                return s;
            })
            .ToList();

        var existing = (await _db.StockItems
                .Where(s => s.CompanyId == companyId)
                .ToListAsync())
            .GroupBy(s => NormalizeName(s.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in incoming)
        {
            if (existing.TryGetValue(item.ItemName, out var ex))
            {
                ex.Parent = item.Parent;
                ex.UOM = item.UOM;
                ex.AdditionalUnits = item.AdditionalUnits;
                ex.RateOfDuty = item.RateOfDuty;
                ex.OpeningBalance = item.OpeningBalance;
                ex.ClosingBalance = item.ClosingBalance;
                ex.OpeningValue = item.OpeningValue;
                ex.ClosingValue = item.ClosingValue;
                ex.IsBatchwiseOn = item.IsBatchwiseOn;
                ex.IsCostTrackingOn = item.IsCostTrackingOn;
                ex.GUID = item.GUID;
                ex.AlterId = item.AlterId;
                ex.LastSyncedAt = item.LastSyncedAt;
            }
            else
            {
                _db.StockItems.Add(item);
                existing[item.ItemName] = item;
            }
        }

        await _db.SaveChangesAsync();

        await SyncStockItemTaxSlabsAsync(taxSlabRecords, existing);
    }

    // Full-replace: every item's slab set is dropped and reinserted from the
    // latest Tally data each sync — see MasterSyncJob's copy of this method
    // for the fuller reasoning.
    private async Task SyncStockItemTaxSlabsAsync(List<TallyService.StockItemTaxSlabRecord> taxSlabs, Dictionary<string, StockItem> itemsByName)
    {
        if (taxSlabs.Count == 0) return;

        var itemIds = taxSlabs
            .Select(t => NormalizeName(t.ItemName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(itemsByName.ContainsKey)
            .Select(n => itemsByName[n].StockItemId)
            .ToList();

        var existingSlabs = await _db.StockItemTaxSlabs
            .Where(t => itemIds.Contains(t.StockItemId))
            .ToListAsync();
        _db.StockItemTaxSlabs.RemoveRange(existingSlabs);

        foreach (var group in taxSlabs.GroupBy(t => NormalizeName(t.ItemName), StringComparer.OrdinalIgnoreCase))
        {
            if (!itemsByName.TryGetValue(group.Key, out var item)) continue;
            foreach (var slab in group)
            {
                _db.StockItemTaxSlabs.Add(new StockItemTaxSlab
                {
                    StockItemId    = item.StockItemId,
                    CompanyId      = item.CompanyId,
                    ApplicableFrom = slab.ApplicableFrom,
                    CGSTRate       = slab.CGSTRate,
                    SGSTRate       = slab.SGSTRate,
                    IGSTRate       = slab.IGSTRate,
                    CessRate       = slab.CessRate,
                    StateCessRate  = slab.StateCessRate
                });
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task UpsertGodownsAsync(int companyId, List<Godown> godowns)
    {
        var incoming = DeduplicateByName(godowns, g => g.GodownName, g => g.GodownName = NormalizeName(g.GodownName))
            .Select(g =>
            {
                g.CompanyId = companyId;
                return g;
            })
            .ToList();

        var existing = (await _db.Godowns
                .Where(g => g.CompanyId == companyId)
                .ToListAsync())
            .GroupBy(g => NormalizeName(g.GodownName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var g in incoming)
        {
            if (existing.TryGetValue(g.GodownName, out var ex))
            {
                ex.Parent = g.Parent;
                ex.Address = g.Address;
                ex.City = g.City;
                ex.State = g.State;
                ex.PinCode = g.PinCode;
                ex.IsBatchwiseOn = g.IsBatchwiseOn;
                ex.GUID = g.GUID;
                ex.AlterId = g.AlterId;
                ex.LastSyncedAt = g.LastSyncedAt;
            }
            else
            {
                _db.Godowns.Add(g);
                existing[g.GodownName] = g;
            }
        }

        await _db.SaveChangesAsync();
    }


    private async Task<(int Pushed, int Failed)> PushPendingOrdersAsync(string tallyUrl, List<Company> companies)
    {
        var companyIds = companies.Select(c => c.CompanyId).ToList();

        var settings = await _db.AppSettings.ToListAsync();
        string Setting(string key, string def) =>
            settings.FirstOrDefault(s => s.Key == key)?.Value ?? def;

        var igstLedger     = Setting("TaxLedgerIGST",      "IGST");
        var cgstLedger     = Setting("TaxLedgerCGST",      "CGST");
        var sgstLedger     = Setting("TaxLedgerSGST",      "SGST");
        var salesLedger    = Setting("SalesLedger",        "Sales");
        var roundOffLedger = Setting("TaxLedgerRoundOff",  "Round Off");
        var voucherType    = Setting("DefaultVoucherType", "Sales Order");
        var batchName      = Setting("DefaultBatchName",   "Primary Batch");

        var pendingOrders = await _db.SaleOrders
            .Include(o => o.Items)
            .Include(o => o.Company)
            .Include(o => o.Ledger)
            .Where(o => companyIds.Contains(o.CompanyId) && o.Status == OrderStatus.Pending)
            .ToListAsync();

        int pushed = 0, failed = 0;
        for (int i = 0; i < pendingOrders.Count; i++)
        {
            var order = pendingOrders[i];
            var companyName = order.Company?.TallyCompanyName ?? string.Empty;
            var isAlter     = !string.IsNullOrEmpty(order.TallyVoucherNo);

            var (success, message) = await _tally.PushSaleOrderAsync(
                tallyUrl, order, companyName,
                salesLedger, igstLedger, cgstLedger, sgstLedger,
                roundOffLedger, isAlter, voucherType, batchName);

            order.Status = success ? OrderStatus.Synced : OrderStatus.Error;
            order.SyncedAt = DateTime.Now;
            order.TallyVoucherNo = success ? order.OrderNo : null;
            order.SyncError = success ? null : message;

            if (success) pushed++; else failed++;

            _db.SyncLogs.Add(new SyncLog
            {
                CompanyId = order.CompanyId,
                SyncType = "OrderPush",
                IsSuccess = success,
                Message = $"Order {order.OrderNo}: {message}"
            });

            // See OrderPushJob's identical pacing note — rapid-fire pushes
            // with no spacing were implicated in crashing Tally's process.
            if (i < pendingOrders.Count - 1)
                await Task.Delay(800);
        }

        if (pendingOrders.Any())
            await _db.SaveChangesAsync();

        return (pushed, failed);
    }

    private async Task<int> CheckInvoiceStatusAsync(List<Company> companies)
    {
        var companyIds = companies.Select(c => c.CompanyId).ToList();

        var orders = await _db.SaleOrders
            .Include(o => o.Company)
            .Where(o => companyIds.Contains(o.CompanyId) && o.Status == OrderStatus.Synced && !o.IsInvoiced)
            .OrderBy(o => o.SaleOrderId)
            .Take(50)
            .ToListAsync();

        if (!orders.Any()) return 0;

        int found = 0;
        // One Tally round-trip per company for this whole batch, not one per
        // order — see TallyService.GetRecentSalesInvoicesAsync for why.
        var groups = orders.Where(o => o.Company != null).GroupBy(o => o.Company!).ToList();
        for (int g = 0; g < groups.Count; g++)
        {
            var company = groups[g].Key;
            var tallyUrl = $"http://{company.TallyIp}:{company.TallyPort}";
            var fromDate = groups[g].Min(o => o.OrderDate);

            var invoices = await _tally.GetRecentSalesInvoicesAsync(tallyUrl, company.TallyCompanyName, fromDate);

            foreach (var order in groups[g])
            {
                if (TallyService.TryMatchInvoice(invoices, order.OrderNo, out var invNo, out var invDate))
                {
                    order.IsInvoiced       = true;
                    order.TallyInvoiceNo   = invNo;
                    order.TallyInvoiceDate = invDate;
                    found++;
                }
            }

            if (g < groups.Count - 1)
                await Task.Delay(800);
        }

        if (found > 0) await _db.SaveChangesAsync();
        return found;
    }

    private static IEnumerable<T> DeduplicateByName<T>(
        IEnumerable<T> source,
        Func<T, string> selector,
        Action<T> normalize)
    {
        return source
            .Where(item => !string.IsNullOrWhiteSpace(selector(item)))
            .Select(item =>
            {
                normalize(item);
                return item;
            })
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last());
    }

    private static string NormalizeName(string? value) => value?.Trim() ?? string.Empty;
}
