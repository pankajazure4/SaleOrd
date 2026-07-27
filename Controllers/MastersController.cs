using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize]
public class MastersController : Controller
{
    private readonly AppDbContext _db;
    private readonly ActiveCompanyResolver _resolver;
    private readonly PermissionService _permSvc;
    private readonly TallyService _tally;

    public MastersController(AppDbContext db, ActiveCompanyResolver resolver, PermissionService permSvc, TallyService tally)
    {
        _db = db;
        _resolver = resolver;
        _permSvc = permSvc;
        _tally = tally;
    }

    public async Task<IActionResult> Parties(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties))
            return Forbid();

        var selectedId = activeId;

        List<Ledger> parties = [];
        if (!string.IsNullOrEmpty(q))
        {
            parties = await _db.Ledgers
                .Where(l => l.CompanyId == selectedId &&
                    (l.LedgerName.Contains(q) || (l.MobileNo != null && l.MobileNo.Contains(q))))
                .OrderBy(l => l.LedgerName)
                .Take(100)
                .ToListAsync();
        }

        ViewBag.Q              = q;
        ViewBag.SelectedId     = selectedId;
        ViewBag.CompanyName    = await _db.Companies.Where(c => c.CompanyId == selectedId).Select(c => c.CompanyName).FirstOrDefaultAsync() ?? string.Empty;
        ViewBag.TotalCount     = await _db.Ledgers.CountAsync(l => l.CompanyId == selectedId);
        return View(parties);
    }

    [HttpGet]
    public async Task<IActionResult> ItemsSearch(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersItems)) return Forbid();

        if (string.IsNullOrWhiteSpace(q))
            return Json(Array.Empty<object>());

        var items = await _db.StockItems
            .Where(s => s.CompanyId == activeId && s.ItemName.Contains(q))
            .OrderBy(s => s.ItemName)
            .Take(60)
            .Select(s => new
            {
                s.StockItemId, s.ItemName, s.UOM, s.Parent,
                s.ClosingBalance, s.IsBatchwiseOn, s.AdditionalUnits
            })
            .ToListAsync();

        return Json(items);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateParty(string partyName, string? gstNo, string? fssaiNo, string? address, string? state, string? contactNo)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return Json(new { success = false, message = "Session expired. Please login again." });
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties))
            return Json(new { success = false, message = "Access denied." });

        partyName = (partyName ?? "").Trim();
        if (string.IsNullOrEmpty(partyName))
            return Json(new { success = false, message = "Party name is required." });

        var exists = await _db.Ledgers.AnyAsync(l => l.CompanyId == activeId && l.LedgerName == partyName);
        if (exists)
            return Json(new { success = false, message = $"A party named \"{partyName}\" already exists." });

        var company = await _db.Companies.FindAsync(activeId);
        if (company == null) return Json(new { success = false, message = "Company not found." });

        var ledger = new Ledger
        {
            LedgerName   = partyName,
            Parent       = "Sundry Debtors",
            GSTNo        = string.IsNullOrWhiteSpace(gstNo) ? null : gstNo.Trim(),
            FSSAINo      = string.IsNullOrWhiteSpace(fssaiNo) ? null : fssaiNo.Trim(),
            Address      = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            State        = string.IsNullOrWhiteSpace(state) ? null : state.Trim(),
            MobileNo     = string.IsNullOrWhiteSpace(contactNo) ? null : contactNo.Trim(),
            CompanyId    = activeId,
            LastSyncedAt = DateTime.Now
        };

        _db.Ledgers.Add(ledger);
        await _db.SaveChangesAsync();

        var tallyUrl = await _db.AppSettings
            .Where(s => s.Key == "TallyUrl")
            .Select(s => s.Value)
            .FirstOrDefaultAsync()
            ?? "http://localhost:9000";

        var fssaiUdfField = await _db.AppSettings
            .Where(s => s.Key == "TallyFssaiUdfField")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();

        var (success, message) = await _tally.PushLedgerAsync(tallyUrl, ledger, company.TallyCompanyName, fssaiUdfField);

        return Json(new
        {
            success = true,
            tallySynced = success,
            message = success
                ? $"Party \"{partyName}\" created and synced to Tally."
                : $"Party \"{partyName}\" saved locally, but Tally sync failed: {message}. It's already usable in Sale Orders; retry the sync later."
        });
    }

    [HttpGet]
    public async Task<IActionResult> PartiesSearch(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties)) return Forbid();

        if (string.IsNullOrWhiteSpace(q))
            return Json(Array.Empty<object>());

        var parties = await _db.Ledgers
            .Where(l => l.CompanyId == activeId &&
                (l.LedgerName.Contains(q) || (l.MobileNo != null && l.MobileNo.Contains(q))))
            .OrderBy(l => l.LedgerName)
            .Take(60)
            .Select(l => new
            {
                l.LedgerId, l.LedgerName, l.MobileNo, l.GSTNo,
                l.Parent, l.ClosingBalance, l.State
            })
            .ToListAsync();

        return Json(parties);
    }

    public async Task<IActionResult> Items(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersItems))
            return Forbid();

        var selectedId = activeId;

        // Don't load any items until user types a search term
        List<StockItem> items = [];
        if (!string.IsNullOrEmpty(q))
        {
            items = await _db.StockItems
                .Where(s => s.CompanyId == selectedId && s.ItemName.Contains(q))
                .OrderBy(s => s.ItemName)
                .Take(100)
                .ToListAsync();
        }

        ViewBag.Q           = q;
        ViewBag.SelectedId  = selectedId;
        ViewBag.CompanyName = await _db.Companies.Where(c => c.CompanyId == selectedId).Select(c => c.CompanyName).FirstOrDefaultAsync() ?? string.Empty;
        ViewBag.TotalCount  = await _db.StockItems.CountAsync(s => s.CompanyId == selectedId);
        return View(items);
    }
}
