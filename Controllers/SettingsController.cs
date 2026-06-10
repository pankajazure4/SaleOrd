using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize(Roles = "Admin")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly TallyService _tally;
    private readonly IConfiguration _config;

    public SettingsController(AppDbContext db, TallyService tally, IConfiguration config)
    {
        _db = db;
        _tally = tally;
        _config = config;
    }

    public async Task<IActionResult> Index()
    {
        var saved = await _db.AppSettings.Where(s => s.Key == "TallyUrl").Select(s => s.Value).FirstOrDefaultAsync();
        var defaultUrl = $"http://{_config["TallySettings:DefaultIp"] ?? "localhost"}:{_config.GetValue<int>("TallySettings:DefaultPort", 9000)}";
        var tallyUrl   = saved ?? defaultUrl;
        var companies  = await _db.Companies
            .Where(c => c.IsActive)
            .OrderBy(c => c.CompanyName)
            .ToListAsync();

        // Try to detect the currently open Tally company and pre-select it
        int selectedCompanyId = companies.FirstOrDefault()?.CompanyId ?? 0;
        try
        {
            var (reachable, openNames) = await _tally.CheckStatusAsync(tallyUrl);
            if (reachable && openNames.Any())
            {
                var match = companies.FirstOrDefault(c =>
                    openNames.Any(n => string.Equals(n.Trim(), c.TallyCompanyName.Trim(), StringComparison.OrdinalIgnoreCase)));
                if (match != null) selectedCompanyId = match.CompanyId;
            }
        }
        catch { /* silently ignore if Tally is unreachable */ }

        ViewBag.TallyUrl          = tallyUrl;
        ViewBag.DefaultUrl        = defaultUrl;
        ViewBag.Companies         = companies;
        ViewBag.SelectedCompanyId = selectedCompanyId;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTallyUrl(string tallyUrl)
    {
        tallyUrl = tallyUrl.Trim();
        var setting = await _db.AppSettings.FindAsync("TallyUrl");
        if (setting == null)
            _db.AppSettings.Add(new AppSetting { Key = "TallyUrl", Value = tallyUrl });
        else
            setting.Value = tallyUrl;

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "Saved" });
    }

    [HttpGet]
    public async Task<IActionResult> RawXml(string? companyName)
    {
        var url = await _db.AppSettings.Where(s => s.Key == "TallyUrl").Select(s => s.Value).FirstOrDefaultAsync()
                  ?? $"http://{_config["TallySettings:DefaultIp"] ?? "localhost"}:{_config.GetValue<int>("TallySettings:DefaultPort", 9000)}";
        var raw = await _tally.GetRawXmlAsync(url, companyName ?? string.Empty);
        return Content(raw, "application/xml");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckStatus(string tallyUrl)
    {
        var url = tallyUrl?.Trim();
        if (string.IsNullOrEmpty(url))
            url = await _db.AppSettings.Where(s => s.Key == "TallyUrl").Select(s => s.Value).FirstOrDefaultAsync()
                  ?? $"http://{_config["TallySettings:DefaultIp"] ?? "localhost"}:{_config.GetValue<int>("TallySettings:DefaultPort", 9000)}";

        var (ok, companies) = await _tally.CheckStatusAsync(url);
        var status = ok ? (companies.Any() ? "online" : "warning") : "offline";

        // Match open Tally companies to our registered companies by TallyCompanyName
        int matchedCompanyId = 0;
        string matchedCompanyName = "";
        if (ok && companies.Any())
        {
            var match = await _db.Companies
                .Where(c => c.IsActive && companies.Contains(c.TallyCompanyName))
                .Select(c => new { c.CompanyId, c.CompanyName })
                .FirstOrDefaultAsync();
            if (match != null)
            {
                matchedCompanyId   = match.CompanyId;
                matchedCompanyName = match.CompanyName;
            }
        }

        return Json(new
        {
            reachable = ok,
            status,
            companies,
            matchedCompanyId,
            matchedCompanyName,
            message = ok
                ? (companies.Any()
                    ? $"Connected. Open in Tally: {string.Join(", ", companies)}"
                    : "Connected, but no company is open in Tally.")
                : "Tally is not reachable. Check the URL and make sure Tally is running."
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FlushCompanyData(int companyId)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.IsActive);
        if (company == null)
            return NotFound();

        await using var tx = await _db.Database.BeginTransactionAsync();

        var orderIds = await _db.SaleOrders
            .Where(o => o.CompanyId == companyId)
            .Select(o => o.SaleOrderId)
            .ToListAsync();

        if (orderIds.Any())
        {
            await _db.SaleOrderItems
                .Where(i => orderIds.Contains(i.SaleOrderId))
                .ExecuteDeleteAsync();
        }

        await _db.SaleOrders.Where(o => o.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.Ledgers.Where(l => l.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.StockItems.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.Godowns.Where(g => g.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.SyncLogs.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync();

        company.LastMasterSyncAt = null;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["Success"] = $"Deleted synced data for {company.CompanyName}.";
        return RedirectToAction(nameof(Index));
    }
}
