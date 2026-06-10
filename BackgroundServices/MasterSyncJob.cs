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

    public MasterSyncJob(IServiceProvider services, ILogger<MasterSyncJob> logger, IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _config.GetValue<int>("TallySync:MasterSyncIntervalMinutes", 30);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAllCompaniesAsync();
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

        foreach (var company in toSync)
            await SyncCompanyAsync(db, tallyService, tallyUrl, company);
    }

    private async Task SyncCompanyAsync(AppDbContext db, TallyService tallyService, string tallyUrl, Company company)
    {
        _logger.LogInformation("Starting master sync for company: {Company}", company.CompanyName);

        try
        {
            var (lNew, lUpd) = await SyncLedgersAsync(db, tallyService, tallyUrl, company);
            var (iNew, iUpd) = await SyncStockItemsAsync(db, tallyService, tallyUrl, company);
            var (gNew, gUpd) = await SyncGodownsAsync(db, tallyService, tallyUrl, company);

            company.LastMasterSyncAt = DateTime.Now;

            var msg = $"Ledgers +{lNew} ~{lUpd} | Items +{iNew} ~{iUpd} | Godowns +{gNew} ~{gUpd}";
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
