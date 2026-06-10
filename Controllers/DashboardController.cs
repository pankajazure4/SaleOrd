using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    private readonly ActiveCompanyResolver _resolver;
    private readonly PermissionService _permSvc;

    public DashboardController(AppDbContext db, ActiveCompanyResolver resolver, PermissionService permSvc)
    {
        _db = db;
        _resolver = resolver;
        _permSvc = permSvc;
    }

    public async Task<IActionResult> Index()
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.Dashboard))
            return Forbid();

        var query = _db.SaleOrders.Where(o => o.CompanyId == companyId);
        if (user.Role == AppRoles.Salesman)
            query = query.Where(o => o.CreatedById == user.Id);

        var today      = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var stats = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total      = g.Count(),
                Pending    = g.Count(o => o.Status == OrderStatus.Pending),
                Synced     = g.Count(o => o.Status == OrderStatus.Synced),
                Error      = g.Count(o => o.Status == OrderStatus.Error),
                TotalValue = g.Sum(o => o.TotalAmount),
                TodayCount = g.Count(o => o.OrderDate == today),
                MonthValue = g.Where(o => o.OrderDate >= monthStart).Sum(o => o.TotalAmount)
            })
            .FirstOrDefaultAsync();

        var recentOrders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new { o.SaleOrderId, o.OrderNo, o.LedgerName, o.TotalAmount, o.Status, o.OrderDate, o.CreatedByName })
            .ToListAsync();

        var lastSync = await _db.Companies
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.LastMasterSyncAt)
            .FirstOrDefaultAsync();

        ViewBag.Stats        = stats;
        ViewBag.RecentOrders = recentOrders;
        ViewBag.LastSync     = lastSync;
        ViewBag.UserName     = user.FullName;
        ViewBag.Role         = user.Role;
        ViewBag.LedgerCount  = await _db.Ledgers.CountAsync(l => l.CompanyId == companyId);
        ViewBag.ItemCount    = await _db.StockItems.CountAsync(s => s.CompanyId == companyId);
        ViewBag.GodownCount  = await _db.Godowns.CountAsync(g => g.CompanyId == companyId);

        return View();
    }
}
