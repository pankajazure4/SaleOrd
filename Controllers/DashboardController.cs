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
    private readonly IConfiguration _config;

    public DashboardController(AppDbContext db, ActiveCompanyResolver resolver, PermissionService permSvc, IConfiguration config)
    {
        _db = db;
        _resolver = resolver;
        _permSvc = permSvc;
        _config = config;
    }

    public async Task<IActionResult> Index()
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.Dashboard))
            return Forbid();

        // The manual "Sync" button only makes sense when the web app itself
        // talks to Tally — once SaleOrd.SyncAgent is the only sync source
        // (TallySync:Mode=Agent), clicking it would just be a no-op with a
        // "delegated" message, which reads as broken rather than intentional.
        // Hide it entirely in that mode instead.
        ViewBag.IsAgentManaged = TallySyncMode.IsAgentManaged(_config);

        var visibleIds = await UserVisibility.VisibleCreatorIdsAsync(_db, user);
        var query = _db.SaleOrders.Where(o => o.CompanyId == companyId);
        if (visibleIds != null)
            query = query.Where(o => visibleIds.Contains(o.CreatedById));

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
                Invoiced   = g.Count(o => o.IsInvoiced),
                TotalValue = g.Sum(o => o.TotalAmount),
                TodayCount = g.Count(o => o.OrderDate == today),
                MonthValue = g.Where(o => o.OrderDate >= monthStart).Sum(o => o.TotalAmount)
            })
            .FirstOrDefaultAsync();

        var recentOrders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new { o.SaleOrderId, o.OrderNo, o.LedgerName, o.TotalAmount, o.Status, o.IsInvoiced, o.OrderDate, o.CreatedByName })
            .ToListAsync();

        ViewBag.Stats        = stats;
        ViewBag.RecentOrders = recentOrders;
        ViewBag.UserName     = user.FullName;
        ViewBag.Role         = user.Role;

        // "My Team" tab — only for someone who actually has direct reports
        // (AppUser.ManagerId pointing at them), not tied to the Role string
        // itself. Never for Admin: Admin's one view above is already
        // unrestricted, a separate team breakdown adds nothing for them.
        ViewBag.HasTeam = user.Role != AppRoles.Admin
            && await _db.Users.AnyAsync(u => u.ManagerId == user.Id);

        return View();
    }

    // Backs the "My Team" tab — one row per direct report, so a Manager can
    // see their whole team's status at a glance without leaving the
    // Dashboard for Reports > User-wise. Direct reports only (flat
    // hierarchy, see AppUser.ManagerId) — this is deliberately a different,
    // narrower query from UserVisibility (which folds the team into "my
    // orders" for the main stat cards above); here each member needs their
    // own row.
    [HttpGet]
    public async Task<IActionResult> TeamStats()
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.Dashboard))
            return Forbid();

        var today = DateTime.Today;

        var team = await (
            from member in _db.Users
            where member.ManagerId == user.Id
            join order in _db.SaleOrders.Where(o => o.CompanyId == companyId)
                on member.Id equals order.CreatedById into orders
            select new
            {
                userId    = member.Id,
                userName  = member.FullName,
                todayCount = orders.Count(o => o.OrderDate == today),
                pending    = orders.Count(o => o.Status == OrderStatus.Pending),
                synced     = orders.Count(o => o.Status == OrderStatus.Synced),
                invoiced   = orders.Count(o => o.IsInvoiced),
                error      = orders.Count(o => o.Status == OrderStatus.Error),
                total      = orders.Count(),
                totalValue = orders.Sum(o => (decimal?)o.TotalAmount) ?? 0m
            })
            .OrderByDescending(m => m.totalValue)
            .ToListAsync();

        return Json(team);
    }

    // Polled periodically by Dashboard/Index.cshtml so stat cards, the recent
    // orders list, and pending/error counts stay live without a manual reload
    // (background jobs like OrderPushJob change these outside of any user action).
    [HttpGet]
    public async Task<IActionResult> Refresh()
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.Dashboard))
            return Forbid();

        var visibleIds = await UserVisibility.VisibleCreatorIdsAsync(_db, user);
        var query = _db.SaleOrders.Where(o => o.CompanyId == companyId);
        if (visibleIds != null)
            query = query.Where(o => visibleIds.Contains(o.CreatedById));

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
                Invoiced   = g.Count(o => o.IsInvoiced),
                TotalValue = g.Sum(o => o.TotalAmount),
                TodayCount = g.Count(o => o.OrderDate == today),
                MonthValue = g.Where(o => o.OrderDate >= monthStart).Sum(o => o.TotalAmount)
            })
            .FirstOrDefaultAsync();

        var recentOrders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new
            {
                o.SaleOrderId,
                o.OrderNo,
                o.LedgerName,
                o.TotalAmount,
                status = o.Status == OrderStatus.Synced && o.IsInvoiced ? "Invoiced" : o.Status.ToString(),
                orderDate = o.OrderDate.ToString("dd MMM")
            })
            .ToListAsync();

        var lastSync = await _db.Companies
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.LastMasterSyncAt)
            .FirstOrDefaultAsync();

        return Json(new
        {
            stats = stats ?? new { Total = 0, Pending = 0, Synced = 0, Error = 0, Invoiced = 0, TotalValue = 0m, TodayCount = 0, MonthValue = 0m },
            recentOrders,
            lastSync = lastSync.HasValue ? lastSync.Value.ToString("dd MMM yyyy") : "Never"
        });
    }
}
