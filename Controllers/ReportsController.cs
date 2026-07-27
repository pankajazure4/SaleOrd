using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ActiveCompanyResolver _resolver;
    private readonly PermissionService _permSvc;

    public ReportsController(AppDbContext db, ActiveCompanyResolver resolver, PermissionService permSvc)
    {
        _db = db;
        _resolver = resolver;
        _permSvc = permSvc;
    }

    // ── View pages ────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        var perms = await _permSvc.GetAllowedKeysAsync(user.Role);
        if (perms.Contains(AppPermissions.ReportsSummary))   return RedirectToAction(nameof(Summary));
        if (perms.Contains(AppPermissions.ReportsPartyWise)) return RedirectToAction(nameof(PartyWise));
        if (perms.Contains(AppPermissions.ReportsDayWise))   return RedirectToAction(nameof(DayWise));
        if (perms.Contains(AppPermissions.ReportsUserWise))  return RedirectToAction(nameof(UserWise));
        if (perms.Contains(AppPermissions.ReportsPending))   return RedirectToAction(nameof(Pending));
        if (perms.Contains(AppPermissions.ReportsUserPerf))  return RedirectToAction(nameof(UserPerf));
        return Forbid();
    }

    [HttpGet]
    public async Task<IActionResult> Summary()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsSummary)) return Forbid();
        ViewData["ActivePage"] = "ReportsSummary";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> PartyWise()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPartyWise)) return Forbid();
        ViewData["ActivePage"] = "ReportsPartyWise";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> DayWise()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsDayWise)) return Forbid();
        ViewData["ActivePage"] = "ReportsDayWise";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> UserWise()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserWise)) return Forbid();
        ViewData["ActivePage"] = "ReportsUserWise";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPending)) return Forbid();
        ViewData["ActivePage"] = "ReportsPending";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> UserPerf()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserPerf)) return Forbid();
        ViewData["ActivePage"] = "ReportsUserPerf";
        return View();
    }

    // ── JSON data endpoints ───────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> SummaryData(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsSummary)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var orders = await BaseQuery(companyId, user, fromDate, toDate).ToListAsync();

        return Json(new
        {
            totalOrders   = orders.Count,
            totalValue    = orders.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
            syncedCount   = orders.Count(o => o.Status == OrderStatus.Synced),
            pendingCount  = orders.Count(o => o.Status == OrderStatus.Pending),
            errorCount    = orders.Count(o => o.Status == OrderStatus.Error),
            draftCount    = orders.Count(o => o.Status == OrderStatus.Draft),
            invoicedCount = orders.Count(o => o.IsInvoiced),
            recentOrders  = orders.OrderByDescending(o => o.CreatedAt).Take(10).Select(o => new
            {
                o.SaleOrderId, o.OrderNo,
                orderDate    = o.OrderDate.ToString("dd MMM yy"),
                o.LedgerName,
                total        = o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount,
                status       = o.Status.ToString(),
                o.IsInvoiced, o.TallyInvoiceNo
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> PartyWiseData(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPartyWise)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var result = await BaseQuery(companyId, user, fromDate, toDate)
            .GroupBy(o => new { o.LedgerId, o.LedgerName })
            .Select(g => new
            {
                party    = g.Key.LedgerName,
                count    = g.Count(),
                total    = g.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
                pending  = g.Count(o => o.Status == OrderStatus.Pending),
                synced   = g.Count(o => o.Status == OrderStatus.Synced),
                invoiced = g.Count(o => o.IsInvoiced)
            })
            .OrderByDescending(g => g.total)
            .ToListAsync();

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> DayWiseData(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsDayWise)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var orders = await BaseQuery(companyId, user, fromDate, toDate)
            .Select(o => new
            {
                o.OrderDate,
                total = o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount,
                o.Status
            })
            .ToListAsync();

        var result = orders
            .GroupBy(o => o.OrderDate.Date)
            .Select(g => new
            {
                date    = g.Key,
                count   = g.Count(),
                total   = g.Sum(o => o.total),
                synced  = g.Count(o => o.Status == OrderStatus.Synced),
                pending = g.Count(o => o.Status == OrderStatus.Pending)
            })
            .OrderBy(g => g.date)
            .ToList();

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> UserWiseData(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserWise)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var rangeResult = await BaseQuery(companyId, user, fromDate, toDate)
            .GroupBy(o => new { o.CreatedById, o.CreatedByName })
            .Select(g => new
            {
                userId   = g.Key.CreatedById,
                userName = g.Key.CreatedByName,
                count    = g.Count(),
                total    = g.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
                pending  = g.Count(o => o.Status == OrderStatus.Pending),
                synced   = g.Count(o => o.Status == OrderStatus.Synced),
                invoiced = g.Count(o => o.IsInvoiced)
            })
            .OrderByDescending(g => g.total)
            .ToListAsync();

        // Today's/this-month's/all-time figures are fixed periods, independent
        // of the report's own from/to filter — same convention as the Dashboard.
        var today      = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var allOrdersQuery = _db.SaleOrders.Where(o => o.CompanyId == companyId);
        if (user.Role == AppRoles.Salesman)
            allOrdersQuery = allOrdersQuery.Where(o => o.CreatedById == user.Id);

        var absoluteStats = await allOrdersQuery
            .GroupBy(o => new { o.CreatedById })
            .Select(g => new
            {
                userId       = g.Key.CreatedById,
                todayCount   = g.Count(o => o.OrderDate == today),
                monthValue   = g.Where(o => o.OrderDate >= monthStart).Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
                totalOrders  = g.Count(),
                recentOrder  = g.OrderByDescending(o => o.CreatedAt)
                    .Select(o => new { o.OrderNo, o.OrderDate })
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.userId);

        var result = rangeResult.Select(r =>
        {
            absoluteStats.TryGetValue(r.userId, out var abs);
            return new
            {
                r.userId, r.userName, r.count, r.total, r.pending, r.synced, r.invoiced,
                todayCount      = abs?.todayCount ?? 0,
                monthValue      = abs?.monthValue ?? 0,
                totalOrders     = abs?.totalOrders ?? 0,
                recentOrderNo   = abs?.recentOrder?.OrderNo,
                recentOrderDate = abs?.recentOrder != null ? abs.recentOrder.OrderDate.ToString("dd MMM yy") : null
            };
        });

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> PendingData(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPending)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var result = await BaseQuery(companyId, user, fromDate, toDate)
            .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Error)
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new
            {
                o.SaleOrderId, o.OrderNo, o.OrderDate, o.LedgerName,
                total         = o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount,
                status        = o.Status.ToString(),
                o.CreatedByName, o.SyncError
            })
            .ToListAsync();

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> UserPerfData(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserPerf)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var result = await _db.SaleOrders
            .Where(o => o.CompanyId == companyId && o.OrderDate >= fromDate && o.OrderDate <= toDate)
            .GroupBy(o => new { o.CreatedById, o.CreatedByName })
            .Select(g => new
            {
                userName = g.Key.CreatedByName,
                count    = g.Count(),
                total    = g.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
                pending  = g.Count(o => o.Status == OrderStatus.Pending),
                invoiced = g.Count(o => o.IsInvoiced),
                avgOrder = g.Average(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount)
            })
            .OrderByDescending(g => g.total)
            .ToListAsync();

        return Json(result);
    }

    // ── Shared endpoints (used by multiple pages) ─────────────────────────────

    [HttpGet]
    public async Task<IActionResult> OrdersList(DateTime? from, DateTime? to, string? status, string? ledgerName, string? userId)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsSummary)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var query = BaseQuery(companyId, user, fromDate, toDate);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var s))
            query = query.Where(o => o.Status == s);
        if (!string.IsNullOrEmpty(ledgerName))
            query = query.Where(o => o.LedgerName == ledgerName);
        if (!string.IsNullOrEmpty(userId))
            query = query.Where(o => o.CreatedById == userId);

        var result = await query
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new
            {
                o.SaleOrderId, o.OrderNo, o.OrderDate, o.LedgerName,
                total    = o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount,
                status   = o.Status.ToString(),
                o.CreatedByName, o.IsInvoiced, o.TallyInvoiceNo
            })
            .Take(500)
            .ToListAsync();

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetOrderItems(int id)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();

        var items = await _db.SaleOrderItems
            .Where(i => i.SaleOrderId == id && i.SaleOrder!.CompanyId == companyId)
            .Select(i => new { i.ItemName, i.Qty, i.Rate, i.Discount, i.Amount, i.UOM })
            .ToListAsync();

        return Json(items);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static (DateTime from, DateTime to) DateRange(DateTime? from, DateTime? to)
    {
        var f = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var t = to ?? DateTime.Today;
        return (f, t);
    }

    private IQueryable<SaleOrder> BaseQuery(int companyId, AppUser user, DateTime from, DateTime to)
    {
        var q = _db.SaleOrders
            .Where(o => o.CompanyId == companyId && o.OrderDate >= from && o.OrderDate <= to);
        if (user.Role == AppRoles.Salesman)
            q = q.Where(o => o.CreatedById == user.Id);
        return q;
    }
}
