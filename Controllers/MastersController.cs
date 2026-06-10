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

    public MastersController(AppDbContext db, ActiveCompanyResolver resolver, PermissionService permSvc)
    {
        _db = db;
        _resolver = resolver;
        _permSvc = permSvc;
    }

    public async Task<IActionResult> Parties(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties))
            return Forbid();

        var selectedId = activeId;

        var query = _db.Ledgers.Where(l => l.CompanyId == selectedId);
        if (!string.IsNullOrEmpty(q))
            query = query.Where(l => l.LedgerName.Contains(q) ||
                                     (l.MobileNo != null && l.MobileNo.Contains(q)));

        var parties = await query.OrderBy(l => l.LedgerName).Take(200).ToListAsync();

        ViewBag.Q              = q;
        ViewBag.SelectedId     = selectedId;
        ViewBag.CompanyName    = await _db.Companies.Where(c => c.CompanyId == selectedId).Select(c => c.CompanyName).FirstOrDefaultAsync() ?? string.Empty;
        ViewBag.TotalCount     = await _db.Ledgers.CountAsync(l => l.CompanyId == selectedId);
        return View(parties);
    }

    public async Task<IActionResult> Items(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersItems))
            return Forbid();

        var selectedId = activeId;

        var query = _db.StockItems.Where(s => s.CompanyId == selectedId);
        if (!string.IsNullOrEmpty(q))
            query = query.Where(s => s.ItemName.Contains(q));

        var items = await query.OrderBy(s => s.ItemName).Take(300).ToListAsync();

        ViewBag.Q          = q;
        ViewBag.SelectedId = selectedId;
        ViewBag.CompanyName = await _db.Companies.Where(c => c.CompanyId == selectedId).Select(c => c.CompanyName).FirstOrDefaultAsync() ?? string.Empty;
        ViewBag.TotalCount = await _db.StockItems.CountAsync(s => s.CompanyId == selectedId);
        return View(items);
    }
}
