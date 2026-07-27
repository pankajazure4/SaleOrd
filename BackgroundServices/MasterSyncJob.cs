using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.BackgroundServices;

public class MasterSyncJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MasterSyncJob> _logger;
    private readonly IConfiguration _config;
    private readonly SyncCoordinator _coordinator;

    public MasterSyncJob(IServiceProvider services, ILogger<MasterSyncJob> logger, IConfiguration config, SyncCoordinator coordinator)
    {
        _services = services;
        _logger = logger;
        _config = config;
        _coordinator = coordinator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (TallySyncMode.IsAgentManaged(_config))
        {
            _logger.LogInformation("TallySync:Mode is Agent — master sync runs from SaleOrd.SyncAgent instead. Background job disabled.");
            return;
        }

        var intervalMinutes = _config.GetValue<int>("TallySync:MasterSyncIntervalMinutes", 30);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_coordinator.TryStart())
            {
                try
                {
                    await SyncAllCompaniesAsync();
                }
                finally
                {
                    _coordinator.Finish();
                }
            }
            else
            {
                _logger.LogInformation("Skipping master sync cycle — another sync is already in progress.");
            }
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task SyncAllCompaniesAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tallyService = scope.ServiceProvider.GetRequiredService<TallyService>();

        var saved = await db.AppSettings.Where(s => s.Key == "TallyUrl").Select(s => s.Value).FirstOrDefaultAsync();
        var tallyUrl = !string.IsNullOrWhiteSpace(saved)
            ? saved
            : $"http://{_config["TallySettings:DefaultIp"] ?? "localhost"}:{_config.GetValue<int>("TallySettings:DefaultPort", 9000)}";

        var (reachable, openInTally) = await tallyService.CheckStatusAsync(tallyUrl);
        if (!reachable)
        {
            _logger.LogWarning("Tally unreachable at {Url}. Skipping master sync.", tallyUrl);
            return;
        }

        _logger.LogInformation("Tally open companies: {List}", string.Join(", ", openInTally));

        var companies = await db.Companies.Where(c => c.IsActive).ToListAsync();
        var toSync = companies
            .Where(c => openInTally.Any(o =>
                string.Equals(NormalizeName(o), NormalizeName(c.TallyCompanyName), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (!toSync.Any())
        {
            _logger.LogWarning(
                "No matching open companies in Tally. Open: [{Open}]. DB companies: [{DB}]",
                string.Join(", ", openInTally),
                string.Join(", ", companies.Select(c => c.TallyCompanyName)));
            return;
        }

        for (int i = 0; i < toSync.Count; i++)
        {
            await SyncCompanyAsync(db, tallyService, tallyUrl, toSync[i]);
            // Space out back-to-back companies — each SyncCompanyAsync call is
            // already 4 separate Tally requests (ledgers/items/godowns/rates);
            // firing another company's batch immediately after was part of
            // what was overloading Tally's HTTP engine into crashing.
            if (i < toSync.Count - 1)
                await Task.Delay(800);
        }
    }

    private async Task SyncCompanyAsync(AppDbContext db, TallyService tallyService, string tallyUrl, Company company)
    {
        _logger.LogInformation("Starting master sync for company: {Company}", company.CompanyName);

        try
        {
            // Logging BEFORE each call (not just after the batch succeeds) is
            // deliberate — a native Tally crash mid-request doesn't throw
            // until the HTTP call itself fails, so the last "Fetching X..."
            // line in the logs is what actually pinpoints which query did it.
            _logger.LogInformation("[{Company}] Fetching ledgers...", company.CompanyName);
            var (lNew, lUpd) = await SyncLedgersAsync(db, tallyService, tallyUrl, company);
            await Task.Delay(500);
            _logger.LogInformation("[{Company}] Fetching stock items...", company.CompanyName);
            var (iNew, iUpd) = await SyncStockItemsAsync(db, tallyService, tallyUrl, company);
            await Task.Delay(500);
            _logger.LogInformation("[{Company}] Fetching godowns...", company.CompanyName);
            var (gNew, gUpd) = await SyncGodownsAsync(db, tallyService, tallyUrl, company);
            await Task.Delay(500);
            // Last-sale-rates is the heaviest of the four (voucher scan, not a
            // master list) — give Tally a bit more room before/after it.
            _logger.LogInformation("[{Company}] Fetching last sale rates...", company.CompanyName);
            var (rNew, rUpd) = await SyncLastSaleRatesAsync(db, tallyService, tallyUrl, company);

            company.LastMasterSyncAt = DateTime.Now;

            var msg = $"Ledgers +{lNew} ~{lUpd} | Items +{iNew} ~{iUpd} | Godowns +{gNew} ~{gUpd} | Rates +{rNew} ~{rUpd}";
            db.SyncLogs.Add(new SyncLog
            {
                CompanyId = company.CompanyId,
                SyncType = "MasterSync",
                IsSuccess = true,
                Message = msg
            });
            await db.SaveChangesAsync();
            _logger.LogInformation("Master sync done for {Company}: {Msg}", company.CompanyName, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Master sync failed for company {Company}", company.CompanyName);
            db.SyncLogs.Add(new SyncLog
            {
                CompanyId = company.CompanyId,
                SyncType = "MasterSync",
                IsSuccess = false,
                Message = ex.Message
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task<(int Added, int Updated)> SyncLedgersAsync(AppDbContext db, TallyService tallyService, string tallyUrl, Company company)
    {
        var ledgers = await tallyService.GetLedgersAsync(tallyUrl, company);
        _logger.LogInformation("Ledgers from Tally for {Co}: {N}", company.TallyCompanyName, ledgers.Count);
        if (!ledgers.Any()) return (0, 0);

        var incoming = DeduplicateByName(ledgers, l => l.LedgerName, l => l.LedgerName = NormalizeName(l.LedgerName))
            .Select(l =>
            {
                l.CompanyId = company.CompanyId;
                return l;
            })
            .ToList();

        var existing = (await db.Ledgers
                .Where(l => l.CompanyId == company.CompanyId)
                .ToListAsync())
            .GroupBy(l => NormalizeName(l.LedgerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0;
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
                ex.OpeningBalance = l.OpeningBalance;
                ex.ClosingBalance = l.ClosingBalance;
                ex.GUID = l.GUID;
                ex.AlterId = l.AlterId;
                ex.LastSyncedAt = l.LastSyncedAt;
                updated++;
            }
            else
            {
                db.Ledgers.Add(l);
                existing[l.LedgerName] = l;
                added++;
            }
        }

        await db.SaveChangesAsync();
        return (added, updated);
    }

    private async Task<(int Added, int Updated)> SyncStockItemsAsync(AppDbContext db, TallyService tallyService, string tallyUrl, Company company)
    {
        var items = await tallyService.GetStockItemsAsync(tallyUrl, company);
        _logger.LogInformation("Items from Tally for {Co}: {N}", company.TallyCompanyName, items.Count);
        if (!items.Any()) return (0, 0);

        var incoming = DeduplicateByName(items, s => s.ItemName, s => s.ItemName = NormalizeName(s.ItemName))
            .Select(s =>
            {
                s.CompanyId = company.CompanyId;
                return s;
            })
            .ToList();

        var existing = (await db.StockItems
                .Where(s => s.CompanyId == company.CompanyId)
                .ToListAsync())
            .GroupBy(s => NormalizeName(s.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0;
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
                updated++;
            }
            else
            {
                db.StockItems.Add(item);
                existing[item.ItemName] = item;
                added++;
            }
        }

        await db.SaveChangesAsync();
        return (added, updated);
    }

    private async Task<(int Added, int Updated)> SyncGodownsAsync(AppDbContext db, TallyService tallyService, string tallyUrl, Company company)
    {
        var godowns = await tallyService.GetGodownsAsync(tallyUrl, company);
        _logger.LogInformation("Godowns from Tally for {Co}: {N}", company.TallyCompanyName, godowns.Count);
        if (!godowns.Any()) return (0, 0);

        var incoming = DeduplicateByName(godowns, g => g.GodownName, g => g.GodownName = NormalizeName(g.GodownName))
            .Select(g =>
            {
                g.CompanyId = company.CompanyId;
                return g;
            })
            .ToList();

        var existing = (await db.Godowns
                .Where(g => g.CompanyId == company.CompanyId)
                .ToListAsync())
            .GroupBy(g => NormalizeName(g.GodownName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0;
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
                updated++;
            }
            else
            {
                db.Godowns.Add(g);
                existing[g.GodownName] = g;
                added++;
            }
        }

        await db.SaveChangesAsync();
        return (added, updated);
    }

    private async Task<(int Added, int Updated)> SyncLastSaleRatesAsync(AppDbContext db, TallyService tallyService, string tallyUrl, Company company)
    {
        var rates = await tallyService.GetLastSaleRatesAsync(tallyUrl, company);
        _logger.LogInformation("Last sale rates from Tally for {Co}: {N}", company.TallyCompanyName, rates.Count);
        if (!rates.Any()) return (0, 0);

        var ledgerIds = (await db.Ledgers
                .Where(l => l.CompanyId == company.CompanyId)
                .Select(l => new { l.LedgerId, l.LedgerName })
                .ToListAsync())
            .GroupBy(l => NormalizeName(l.LedgerName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().LedgerId, StringComparer.OrdinalIgnoreCase);

        var itemIds = (await db.StockItems
                .Where(s => s.CompanyId == company.CompanyId)
                .Select(s => new { s.StockItemId, s.ItemName })
                .ToListAsync())
            .GroupBy(s => NormalizeName(s.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().StockItemId, StringComparer.OrdinalIgnoreCase);

        var existing = (await db.LastSaleRates
                .Where(r => r.CompanyId == company.CompanyId)
                .ToListAsync())
            .ToDictionary(r => (r.LedgerId, r.StockItemId));

        var now = DateTime.Now;
        int added = 0, updated = 0;
        foreach (var rate in rates)
        {
            if (!ledgerIds.TryGetValue(NormalizeName(rate.PartyName), out var ledgerId)) continue;
            if (!itemIds.TryGetValue(NormalizeName(rate.ItemName), out var stockItemId)) continue;

            var key = (ledgerId, stockItemId);
            if (existing.TryGetValue(key, out var ex))
            {
                ex.Rate = rate.Rate;
                ex.SaleDate = rate.SaleDate;
                ex.LastSyncedAt = now;
                updated++;
            }
            else
            {
                var newRate = new LastSaleRate
                {
                    CompanyId = company.CompanyId,
                    LedgerId = ledgerId,
                    StockItemId = stockItemId,
                    Rate = rate.Rate,
                    SaleDate = rate.SaleDate,
                    LastSyncedAt = now
                };
                db.LastSaleRates.Add(newRate);
                existing[key] = newRate;
                added++;
            }
        }

        await db.SaveChangesAsync();
        return (added, updated);
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
