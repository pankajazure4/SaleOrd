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

    public async Task<IActionResult> Index()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        var perms = await _permSvc.GetAllowedKeysAsync(user.Role);
        bool hasAny = perms.Any(p => p.StartsWith("Reports."));
        if (!hasAny) return Forbid();

        ViewBag.Perms   = perms;
        ViewBag.IsSalesman = user.Role == AppRoles.Salesman;
        ViewBag.UserId  = user.Id;
        return View();
    }

    // ── Sales Summary ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> SalesSummary(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsSummary)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var query = BaseQuery(companyId, user, fromDate, toDate);

        var orders = await query.ToListAsync();
        var partyWise = orders
            .GroupBy(o => o.LedgerName)
            .Select(g => new { Party = g.Key, Count = g.Count(), Total = g.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount), Synced = g.Count(o => o.Status == OrderStatus.Synced) })
            .OrderByDescending(g => g.Total).Take(20).ToList<object>();

        var dayWise = orders
            .GroupBy(o => o.OrderDate.Date)
            .Select(g => new { Date = g.Key, Count = g.Count(), Total = g.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount) })
            .OrderBy(g => g.Date).ToList<object>();

        return Json(new
        {
            totalOrders = orders.Count,
            totalValue  = orders.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
            syncedCount = orders.Count(o => o.Status == OrderStatus.Synced),
            pendingCount= orders.Count(o => o.Status == OrderStatus.Pending),
            invoicedCount = orders.Count(o => o.IsInvoiced),
            partyWise,
            dayWise
        });
    }

    // ── User-wise Sales ───────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> UserWise(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserWise)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var query = BaseQuery(companyId, user, fromDate, toDate);

        var result = await query
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

        return Json(result);
    }

    // ── Pending Orders ────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> PendingOrders(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPending)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var query = BaseQuery(companyId, user, fromDate, toDate)
            .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Error);

        var result = await query
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new
            {
                o.SaleOrderId, o.OrderNo, o.OrderDate, o.LedgerName,
                total      = o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount,
                status     = o.Status.ToString(),
                o.CreatedByName, o.SyncError
            })
            .ToListAsync();

        return Json(result);
    }

    // ── User Performance ──────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> UserPerformance(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserPerf)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var query = _db.SaleOrders
            .Where(o => o.CompanyId == companyId && o.OrderDate >= fromDate && o.OrderDate <= toDate);

        var result = await query
            .GroupBy(o => new { o.CreatedById, o.CreatedByName })
            .Select(g => new
            {
                userName  = g.Key.CreatedByName,
                count     = g.Count(),
                total     = g.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
                pending   = g.Count(o => o.Status == OrderStatus.Pending),
                invoiced  = g.Count(o => o.IsInvoiced),
                avgOrder  = g.Average(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount)
            })
            .OrderByDescending(g => g.total)
            .ToListAsync();

        return Json(result);
    }

    // ── Party-wise Detail (existing) ──────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> PartyWise(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPartyWise)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var query = BaseQuery(companyId, user, fromDate, toDate);

        var result = await query
            .GroupBy(o => new { o.LedgerId, o.LedgerName })
            .Select(g => new
            {
                party   = g.Key.LedgerName,
                count   = g.Count(),
                total   = g.Sum(o => o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount),
                pending = g.Count(o => o.Status == OrderStatus.Pending),
                synced  = g.Count(o => o.Status == OrderStatus.Synced),
                invoiced= g.Count(o => o.IsInvoiced)
            })
            .OrderByDescending(g => g.total)
            .ToListAsync();

        return Json(result);
    }

    // ── Detailed Orders list (for drilldown) ─────────────────────────────────
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

    // ── Order item breakup (existing) ─────────────────────────────────────────
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
