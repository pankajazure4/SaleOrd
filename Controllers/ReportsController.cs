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
        ViewBag.CanExport = await _permSvc.HasAsync(user.Role, AppPermissions.ReportsExport);
        ViewData["ActivePage"] = "ReportsSummary";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> PartyWise()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPartyWise)) return Forbid();
        ViewBag.CanExport = await _permSvc.HasAsync(user.Role, AppPermissions.ReportsExport);
        ViewData["ActivePage"] = "ReportsPartyWise";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> DayWise()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsDayWise)) return Forbid();
        ViewBag.CanExport = await _permSvc.HasAsync(user.Role, AppPermissions.ReportsExport);
        ViewData["ActivePage"] = "ReportsDayWise";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> UserWise()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserWise)) return Forbid();
        ViewBag.CanExport = await _permSvc.HasAsync(user.Role, AppPermissions.ReportsExport);
        ViewData["ActivePage"] = "ReportsUserWise";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsPending)) return Forbid();
        ViewBag.CanExport = await _permSvc.HasAsync(user.Role, AppPermissions.ReportsExport);
        ViewData["ActivePage"] = "ReportsPending";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> UserPerf()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsUserPerf)) return Forbid();
        ViewBag.CanExport = await _permSvc.HasAsync(user.Role, AppPermissions.ReportsExport);
        ViewData["ActivePage"] = "ReportsUserPerf";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> TransactionCompare()
    {
        var (_, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsTransactionCompare)) return Forbid();
        ViewBag.CanExport = await _permSvc.HasAsync(user.Role, AppPermissions.ReportsExport);
        ViewData["ActivePage"] = "ReportsTransactionCompare";
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
            cancelledCount = orders.Count(o => o.Status == OrderStatus.Cancelled),
            invoicedCount = orders.Count(o => o.IsInvoiced)
        });
    }

    // Backs the "Order Details" grid on the Sales Summary page — properly
    // paginated + searchable server-side, unlike the old SummaryData.recentOrders
    // (a fixed Take(10) with nothing beyond it reachable at all).
    [HttpGet]
    public async Task<IActionResult> SummaryOrdersData(DateTime? from, DateTime? to, string? q, int page = 1)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsSummary)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var query = BaseQuery(companyId, user, fromDate, toDate);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(o => o.OrderNo.Contains(term) || o.LedgerName.Contains(term));
        }

        const int pageSize = 20;
        if (page < 1) page = 1;

        var total = await query.CountAsync();
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.SaleOrderId, o.OrderNo,
                orderDate = o.OrderDate.ToString("dd MMM yy"),
                o.LedgerName,
                total     = o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount,
                status    = o.Status == OrderStatus.Synced && o.IsInvoiced ? "Invoiced" : o.Status.ToString(),
                o.IsInvoiced, o.TallyInvoiceNo
            })
            .ToListAsync();

        return Json(new { total, page, pageSize, orders });
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

    // Order vs Tally Invoice — full line-item compare. Deliberately built
    // entirely from data this app already syncs (SaleOrders/SaleOrderItems
    // it created, VoucherInventoryEntries from the Rates sync) instead of
    // querying Tally directly — no new Tally integration needed, but it does
    // mean an order's invoice lines only show up here once the Rates sync
    // (Config > Sync Rates interval, or the manual button) has actually
    // pulled that invoice's voucher in.
    [HttpGet]
    public async Task<IActionResult> TransactionCompareData(DateTime? from, DateTime? to)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.ReportsTransactionCompare)) return Forbid();

        var (fromDate, toDate) = DateRange(from, to);
        var orders = await BaseQuery(companyId, user, fromDate, toDate)
            .Where(o => o.IsInvoiced && o.TallyInvoiceNo != null)
            .Include(o => o.Items)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        if (orders.Count == 0) return Json(new List<object>());

        // One query for every matched invoice's line items, not one per
        // order — same round-trip-minimizing shape as the rest of this
        // controller/the sync services.
        var invoiceNos = orders.Select(o => o.TallyInvoiceNo!).Distinct().ToList();
        var invoiceLines = await _db.VoucherInventoryEntries
            .Where(v => v.CompanyId == companyId && invoiceNos.Contains(v.VoucherNumber))
            .ToListAsync();

        var result = orders.Select(o =>
        {
            var linesForThisInvoice = invoiceLines.Where(v => v.VoucherNumber == o.TallyInvoiceNo).ToList();
            return new
            {
                o.SaleOrderId,
                o.OrderNo,
                orderDate   = o.OrderDate,
                o.LedgerName,
                invoiceNo   = o.TallyInvoiceNo,
                invoiceDate = o.TallyInvoiceDate,
                orderTotal  = o.GrandTotal > 0 ? o.GrandTotal : o.TotalAmount,
                // True only once every invoice line has actually been pulled
                // in AND every line matches — false is the signal to check
                // the Rates sync, not necessarily a real order/invoice mismatch.
                fullyMatched = linesForThisInvoice.Count > 0 && o.Items.All(oi =>
                    linesForThisInvoice.Any(v => string.Equals(v.StockItemName, oi.ItemName, StringComparison.OrdinalIgnoreCase))),
                items = o.Items.Select(oi =>
                {
                    var inv = linesForThisInvoice.FirstOrDefault(v =>
                        string.Equals(v.StockItemName, oi.ItemName, StringComparison.OrdinalIgnoreCase));
                    return new
                    {
                        itemName     = oi.ItemName,
                        orderedQty   = oi.Qty,
                        orderedRate  = oi.Rate,
                        orderedAmt   = oi.Amount,
                        invoicedQty  = inv?.BilledQty,
                        invoicedRate = inv?.Rate,
                        invoicedAmt  = inv?.Amount,
                        matched      = inv != null,
                        qtyDiff      = inv != null ? inv.BilledQty - oi.Qty : (decimal?)null,
                        amtDiff      = inv != null ? inv.Amount - oi.Amount : (decimal?)null
                    };
                }).ToList()
            };
        }).ToList();

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
