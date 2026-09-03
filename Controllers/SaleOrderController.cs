using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Models.ViewModels;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize]
public class SaleOrderController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly ActiveCompanyResolver _resolver;
    private readonly PermissionService _permSvc;
    private readonly UserActivityService _activity;

    public SaleOrderController(AppDbContext db, UserManager<AppUser> userManager, ActiveCompanyResolver resolver,
        PermissionService permSvc, UserActivityService activity)
    {
        _db = db;
        _userManager = userManager;
        _resolver = resolver;
        _permSvc = permSvc;
        _activity = activity;
    }

    public async Task<IActionResult> Index(string? status, string? search, DateTime? from, DateTime? to, DateTime? date, int page = 1)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.SaleOrderView))
            return Forbid();

        var query = _db.SaleOrders.Where(o => o.CompanyId == companyId);

        if (user.Role == AppRoles.Salesman)
            query = query.Where(o => o.CreatedById == user.Id);

        // "Invoiced" isn't a real OrderStatus value — it's Status==Synced
        // with IsInvoiced also true, shown as its own status everywhere the
        // badge appears. Filtering "Synced" here means synced-but-not-yet-
        // invoiced specifically, so the two filters stay mutually exclusive,
        // matching what the badge itself shows for any given order.
        if (string.Equals(status, "Invoiced", StringComparison.OrdinalIgnoreCase))
            query = query.Where(o => o.Status == OrderStatus.Synced && o.IsInvoiced);
        else if (string.Equals(status, nameof(OrderStatus.Synced), StringComparison.OrdinalIgnoreCase))
            query = query.Where(o => o.Status == OrderStatus.Synced && !o.IsInvoiced);
        else if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var s))
            query = query.Where(o => o.Status == s);

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(o => o.OrderNo.ToLower().Contains(searchLower) || o.LedgerName.ToLower().Contains(searchLower));
        }

        if (date.HasValue)
        {
            var targetDate = date.Value.Date;
            query = query.Where(o => o.OrderDate >= targetDate && o.OrderDate < targetDate.AddDays(1));
            ViewBag.Date = targetDate.ToString("yyyy-MM-dd");
        }
        else
        {
            if (from.HasValue)
            {
                var fromDate = from.Value.Date;
                query = query.Where(o => o.OrderDate >= fromDate);
                ViewBag.From = fromDate.ToString("yyyy-MM-dd");
            }
            if (to.HasValue)
            {
                var toDate = to.Value.Date;
                query = query.Where(o => o.OrderDate < toDate.AddDays(1));
                ViewBag.To = toDate.ToString("yyyy-MM-dd");
            }
        }

        var total = await query.CountAsync();
        const int pageSize = 20;

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new SaleOrderListVM
            {
                SaleOrderId = o.SaleOrderId,
                OrderNo = o.OrderNo,
                OrderDate = o.OrderDate,
                LedgerName = o.LedgerName,
                TotalAmount = o.TotalAmount,
                Status = o.Status,
                IsInvoiced = o.IsInvoiced,
                CreatedByName = o.CreatedByName,
                CreatedAt = o.CreatedAt,
                SyncError = o.SyncError
            })
            .ToListAsync();

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Status = status;
        ViewBag.Search = search;

        return View(orders);
    }

    [HttpGet]
    public async Task<IActionResult> OrdersSearch(string? q, string? status)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.SaleOrderView)) return Forbid();

        var query = _db.SaleOrders.Where(o => o.CompanyId == companyId);

        if (user.Role == AppRoles.Salesman)
            query = query.Where(o => o.CreatedById == user.Id);

        // Same "Invoiced" pseudo-status handling as Index() — see its comment.
        if (string.Equals(status, "Invoiced", StringComparison.OrdinalIgnoreCase))
            query = query.Where(o => o.Status == OrderStatus.Synced && o.IsInvoiced);
        else if (string.Equals(status, nameof(OrderStatus.Synced), StringComparison.OrdinalIgnoreCase))
            query = query.Where(o => o.Status == OrderStatus.Synced && !o.IsInvoiced);
        else if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var s))
            query = query.Where(o => o.Status == s);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var ql = q.Trim().ToLower();
            query = query.Where(o => o.OrderNo.ToLower().Contains(ql) || o.LedgerName.ToLower().Contains(ql));
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .Select(o => new
            {
                o.SaleOrderId, o.OrderNo, o.LedgerName,
                orderDate = o.OrderDate.ToString("dd MMM yyyy"),
                o.TotalAmount,
                status = o.Status == OrderStatus.Synced && o.IsInvoiced ? "Invoiced" : o.Status.ToString(),
                o.SyncError
            })
            .ToListAsync();

        return Json(orders);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.SaleOrderCreate))
            return Forbid();

        await LoadDropdownsAsync(companyId);
        await LoadTaxSettingsAsync();
        return View(new SaleOrderCreateVM { OrderDate = DateTime.Today });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SaleOrderCreateVM model)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, message = "Session expired. Please login again." });
            return RedirectToAction("Login", "Account");
        }

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.SaleOrderCreate))
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, message = "Access denied." });
            return Forbid();
        }

        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (!model.Items.Any() || model.Items.All(i => i.Qty <= 0))
            ModelState.AddModelError("", "Add at least one item with quantity.");

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = string.Join(" ", errors) });
            }
            await LoadDropdownsAsync(companyId);
            return View(model);
        }

        var ledger = await _db.Ledgers.FindAsync(model.LedgerId);
        if (ledger == null)
        {
            if (isAjax)
                return Json(new { success = false, message = "Invalid party selected." });
            ModelState.AddModelError("LedgerId", "Invalid party selected.");
            await LoadDropdownsAsync(companyId);
            return View(model);
        }

        var orderNo = await GenerateOrderNoAsync(companyId);
        var createdAt = DateTime.Now;
        var editDeadline = await CalcEditDeadlineAsync(createdAt);

        var order = new SaleOrder
        {
            OrderNo      = orderNo,
            OrderDate    = model.OrderDate,
            DeliveryDate = model.DeliveryDate,
            LedgerId     = model.LedgerId,
            LedgerName   = ledger.LedgerName,
            CompanyId    = companyId,
            Narration    = model.Narration,
            Status       = OrderStatus.Pending,
            CreatedById  = user.Id,
            CreatedByName= user.FullName,
            CreatedAt    = createdAt,
            EditDeadline = editDeadline,
            TaxType      = model.TaxType
        };

        foreach (var item in model.Items.Where(i => i.Qty > 0))
        {
            var stockItem = await _db.StockItems.FindAsync(item.StockItemId);
            if (stockItem == null) continue;

            var amount = item.Qty * item.Rate * (1 - item.Discount / 100);
            var soItem = new SaleOrderItem
            {
                StockItemId = item.StockItemId,
                ItemName    = stockItem.ItemName,
                UOM         = stockItem.UOM,
                Qty         = item.Qty,
                Rate        = item.Rate,
                Discount    = item.Discount,
                Amount      = Math.Round(amount, 2, MidpointRounding.AwayFromZero)
            };
            await ApplyItemTaxAsync(soItem, companyId, order.TaxType, order.OrderDate);
            order.Items.Add(soItem);
        }

        order.TotalAmount = order.Items.Sum(i => i.Amount);
        ComputeOrderTaxFromItems(order);

        _db.SaleOrders.Add(order);
        await _db.SaveChangesAsync();

        await _activity.LogAsync(user.Id, user.FullName, user.Role, ActivityActions.CreateOrder,
            "SaleOrder", order.SaleOrderId, $"Created {orderNo}", companyId);

        if (isAjax)
            return Json(new { success = true, message = $"Sale Order {orderNo} created successfully!", redirectUrl = "/Dashboard" });

        TempData["Success"] = $"Sale Order {orderNo} created successfully!";
        return RedirectToAction("Index", "Dashboard");
    }

    public async Task<IActionResult> Details(int id)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        var order = await _db.SaleOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.SaleOrderId == id && o.CompanyId == companyId);

        if (order == null) return NotFound();

        if (user.Role == AppRoles.Salesman && order.CreatedById != user.Id)
            return Forbid();

        ViewBag.CanPrint = await _permSvc.HasAsync(user.Role, AppPermissions.PrintSaleOrder);
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();

        var order = await _db.SaleOrders.FindAsync(id);
        if (order == null || order.CompanyId != companyId) return NotFound();

        if (order.Status == OrderStatus.Synced)
        {
            TempData["Error"] = "Cannot cancel an order already synced to Tally.";
            return RedirectToAction("Details", new { id });
        }

        order.Status = OrderStatus.Cancelled;
        await _db.SaveChangesAsync();

        await _activity.LogAsync(user.Id, user.FullName, user.Role, ActivityActions.CancelOrder,
            "SaleOrder", order.SaleOrderId, $"Cancelled {order.OrderNo}", companyId);

        TempData["Success"] = "Order cancelled.";
        return RedirectToAction("Index");
    }

    // Resubmit action removed — it only ever did `Status = Pending` +
    // `SyncError = null`, nothing that touched Tally. Editing an Error order
    // (see the Status-bypass above) already does exactly that on save, so
    // this was a duplicate path to the same effect. Historical "Resubmitted"
    // entries in Admin > User Activity are untouched; new recoveries now
    // just show up there as EditOrder.

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.SaleOrderEdit))
            return Forbid();

        var order = await _db.SaleOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.SaleOrderId == id && o.CompanyId == companyId);

        if (order == null) return NotFound();

        if (user.Role == AppRoles.Salesman && order.CreatedById != user.Id)
            return Forbid();

        // Invoice lock — permanent
        if (order.IsInvoiced)
        {
            TempData["Error"] = $"Order {order.OrderNo} has been invoiced in Tally (Invoice: {order.TallyInvoiceNo}) and cannot be edited.";
            return RedirectToAction("Details", new { id });
        }

        // Edit deadline check — skipped entirely for Error orders. There's no
        // separate "Resubmit to Tally" button anymore (editing already resets
        // Status to Pending below, which is all Resubmit ever did), so Edit
        // has to stay reachable for a stuck order regardless of the global
        // edit toggle or deadline, or a failed push becomes unrecoverable
        // from the UI.
        if (order.Status != OrderStatus.Error)
        {
            var editEnabled = (await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "OrderEditEnabled"))?.Value == "true";
            if (!editEnabled)
            {
                TempData["Error"] = "Order editing is currently disabled.";
                return RedirectToAction("Details", new { id });
            }
            if (order.EditDeadline.HasValue && DateTime.Now > order.EditDeadline.Value)
            {
                TempData["Error"] = $"Edit window closed at {order.EditDeadline.Value:dd-MMM-yyyy hh:mm tt}.";
                return RedirectToAction("Details", new { id });
            }
        }

        await LoadDropdownsAsync(companyId);
        await LoadTaxSettingsAsync();

        var model = new SaleOrderCreateVM
        {
            SaleOrderId  = order.SaleOrderId,
            LedgerId     = order.LedgerId,
            LedgerName   = order.LedgerName,
            OrderDate    = order.OrderDate,
            DeliveryDate = order.DeliveryDate,
            Narration    = order.Narration,
            TaxType      = order.TaxType,
            TaxPercent   = order.TaxPercent,
            TaxTotal     = order.TaxTotal,
            RoundOff     = order.RoundOff,
            GrandTotal   = order.GrandTotal,
            Items = order.Items.Select(i => new SaleOrderItemVM
            {
                StockItemId = i.StockItemId,
                ItemName    = i.ItemName,
                UOM         = i.UOM,
                Qty         = i.Qty,
                Rate        = i.Rate,
                Discount    = i.Discount,
                Amount      = i.Amount,
                GodownId    = i.GodownId,
                GodownName  = i.GodownName,
                CGSTRate    = i.CGSTRate,
                SGSTRate    = i.SGSTRate,
                IGSTRate    = i.IGSTRate
            }).ToList()
        };

        return View("Create", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SaleOrderCreateVM model)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (user == null)
        {
            if (isAjax) return Json(new { success = false, message = "Session expired. Please login again." });
            return RedirectToAction("Login", "Account");
        }

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.SaleOrderEdit))
        {
            if (isAjax) return Json(new { success = false, message = "Access denied." });
            return Forbid();
        }

        var order = await _db.SaleOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.SaleOrderId == id && o.CompanyId == companyId);

        if (order == null)
        {
            if (isAjax) return Json(new { success = false, message = "Order not found." });
            return NotFound();
        }

        if (user.Role == AppRoles.Salesman && order.CreatedById != user.Id)
        {
            if (isAjax) return Json(new { success = false, message = "Access denied." });
            return Forbid();
        }

        if (order.IsInvoiced)
            ModelState.AddModelError("", $"Order {order.OrderNo} is invoiced in Tally and cannot be edited.");

        // Same Error-status bypass as the GET action above.
        if (order.Status != OrderStatus.Error)
        {
            var editEnabled = (await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "OrderEditEnabled"))?.Value == "true";
            if (!editEnabled)
                ModelState.AddModelError("", "Order editing is currently disabled.");

            if (order.EditDeadline.HasValue && DateTime.Now > order.EditDeadline.Value)
                ModelState.AddModelError("", $"Edit window closed at {order.EditDeadline.Value:dd-MMM-yyyy hh:mm tt}.");
        }

        if (!model.Items.Any() || model.Items.All(i => i.Qty <= 0))
            ModelState.AddModelError("", "Add at least one item with quantity.");

        if (!ModelState.IsValid)
        {
            if (isAjax)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = string.Join(" ", errors) });
            }
            await LoadDropdownsAsync(companyId);
            model.SaleOrderId = id;
            return View("Create", model);
        }

        var ledger = await _db.Ledgers.FindAsync(model.LedgerId);
        if (ledger == null)
        {
            if (isAjax) return Json(new { success = false, message = "Invalid party selected." });
            ModelState.AddModelError("LedgerId", "Invalid party selected.");
            await LoadDropdownsAsync(companyId);
            model.SaleOrderId = id;
            return View("Create", model);
        }

        order.OrderDate    = model.OrderDate;
        order.DeliveryDate = model.DeliveryDate;
        order.LedgerId     = model.LedgerId;
        order.LedgerName   = ledger.LedgerName;
        order.Narration    = model.Narration;
        order.TaxType      = model.TaxType;

        // Reset to Pending so Tally job retries it
        order.Status    = OrderStatus.Pending;
        order.SyncError = null;

        _db.SaleOrderItems.RemoveRange(order.Items);
        order.Items.Clear();

        foreach (var item in model.Items.Where(i => i.Qty > 0))
        {
            var stockItem = await _db.StockItems.FindAsync(item.StockItemId);
            if (stockItem == null) continue;

            var amount = item.Qty * item.Rate * (1 - item.Discount / 100);
            var soItem = new SaleOrderItem
            {
                StockItemId = item.StockItemId,
                ItemName    = stockItem.ItemName,
                UOM         = stockItem.UOM,
                Qty         = item.Qty,
                Rate        = item.Rate,
                Discount    = item.Discount,
                Amount      = Math.Round(amount, 2, MidpointRounding.AwayFromZero)
            };
            await ApplyItemTaxAsync(soItem, companyId, order.TaxType, order.OrderDate);
            order.Items.Add(soItem);
        }

        order.TotalAmount = order.Items.Sum(i => i.Amount);
        ComputeOrderTaxFromItems(order);

        await _db.SaveChangesAsync();

        await _activity.LogAsync(user.Id, user.FullName, user.Role, ActivityActions.EditOrder,
            "SaleOrder", order.SaleOrderId, $"Edited {order.OrderNo}", companyId);

        if (isAjax)
            return Json(new { success = true, message = $"Sale Order {order.OrderNo} updated successfully!", redirectUrl = "/SaleOrder" });

        TempData["Success"] = $"Sale Order {order.OrderNo} updated successfully!";
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();

        var order = await _db.SaleOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.SaleOrderId == id && o.CompanyId == companyId);

        if (order == null) return NotFound();

        if (user.Role == AppRoles.Salesman && order.CreatedById != user.Id)
            return Forbid();

        if (order.Status == OrderStatus.Synced)
        {
            TempData["Error"] = "Cannot delete a synced order.";
            return RedirectToAction("Details", new { id });
        }

        _db.SaleOrderItems.RemoveRange(order.Items);
        _db.SaleOrders.Remove(order);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Sale Order {order.OrderNo} deleted successfully!";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.PrintSaleOrder)) return Forbid();

        var order = await _db.SaleOrders
            .Include(o => o.Items)
            .Include(o => o.Company)
            .FirstOrDefaultAsync(o => o.SaleOrderId == id && o.CompanyId == companyId);

        if (order == null) return NotFound();

        if (user.Role == AppRoles.Salesman && order.CreatedById != user.Id)
            return Forbid();

        await _activity.LogAsync(user.Id, user.FullName, user.Role, ActivityActions.PrintOrder,
            "SaleOrder", order.SaleOrderId, $"Printed {order.OrderNo}", companyId);

        return View(order);
    }

    // Returns customer-wise last rate for an item (falls back to item master
    // rate), plus its current GST% breakdown — used for the live total
    // preview while building an order; the server recomputes authoritatively
    // from the same StockItemTaxSlabs at save time regardless of this.
    [HttpGet]
    public async Task<IActionResult> GetItemRate(int stockItemId, int ledgerId, DateTime? orderDate = null)
    {
        var (companyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();

        var (cgst, sgst, igst) = await GetEffectiveTaxRatesAsync(companyId, stockItemId, orderDate ?? DateTime.Today);

        // Customer+Item last sale rate, pulled from Tally's actual Sales
        // vouchers (VoucherInventoryEntries — full line-level sync, not just
        // a derived summary) — the authoritative source per the client's
        // requirement. Only falls through when Tally has never recorded a
        // sale for this exact party+item combination.
        var tallyRate = await _db.VoucherInventoryEntries
            .Where(v => v.StockItemId == stockItemId && v.LedgerId == ledgerId && v.CompanyId == companyId)
            .OrderByDescending(v => v.VoucherDate)
            .Select(v => (decimal?)v.Rate)
            .FirstOrDefaultAsync();

        if (tallyRate.HasValue)
            return Json(new { rate = tallyRate.Value, source = "tally", cgstRate = cgst, sgstRate = sgst, igstRate = igst });

        // Fall back to this customer's last rate from orders placed in-app
        // (covers a new SO rate before the next Tally sync pulls it back).
        var lastRate = await _db.SaleOrderItems
            .Where(i => i.StockItemId == stockItemId && i.SaleOrder!.LedgerId == ledgerId
                        && i.SaleOrder.CompanyId == companyId && i.Rate > 0)
            .OrderByDescending(i => i.SaleOrder!.OrderDate)
            .Select(i => (decimal?)i.Rate)
            .FirstOrDefaultAsync();

        if (lastRate.HasValue)
            return Json(new { rate = lastRate.Value, source = "customer", cgstRate = cgst, sgstRate = sgst, igstRate = igst });

        // Fall back to item master rate
        var masterRate = await _db.StockItems
            .Where(s => s.StockItemId == stockItemId && s.CompanyId == companyId)
            .Select(s => (decimal?)s.Rate)
            .FirstOrDefaultAsync();

        return Json(new { rate = masterRate ?? 0, source = "master", cgstRate = cgst, sgstRate = sgst, igstRate = igst });
    }

    [HttpGet]
    public async Task<IActionResult> GetItems(string? q, int companyId = 0)
    {
        var (activeCompanyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();

        var cId = companyId > 0 ? companyId : activeCompanyId;

        // ClosingBalance is the item's overall (all-godowns) latest closing
        // quantity — synced unconditionally on every Master Sync cycle (see
        // the Agent's GetStockItemsAsync), same field already shown on
        // Masters > Stock Items. Shown here as a remark under each item in
        // the punch-order dropdown so a salesman can see current stock
        // before picking an item — replaces the earlier godown-wise
        // breakdown attempt (removed; that logic no longer exists anywhere).
        var rows = await _db.StockItems
            .Where(s => s.CompanyId == cId &&
                (string.IsNullOrEmpty(q) || s.ItemName.Contains(q)))
            .OrderBy(s => s.ItemName)
            .Take(50)
            .Select(s => new { s.StockItemId, s.ItemName, s.UOM, s.Rate, s.ClosingBalance })
            .ToListAsync();

        // Decimal format strings don't reliably translate to SQL, so this
        // formatting step runs in memory, after the query, not inside the
        // .Select() above.
        var items = rows.Select(s => new
        {
            s.StockItemId, s.ItemName, s.UOM, s.Rate,
            remark = s.ClosingBalance != 0 ? $"{s.ClosingBalance:0.###} {s.UOM} in stock" : null
        });

        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> GetParties(string? q)
    {
        var (activeCompanyId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();

        // Parties created via the web UI (Masters > Parties) sit at
        // ApprovalStatus.Pending until an Admin approves them — they must
        // not be pickable here until then. Ledgers pulled in from Tally
        // master sync are always Approved already, so this filter is
        // invisible for the normal (Tally-synced) case.
        var parties = await _db.Ledgers
            .Where(l => l.CompanyId == activeCompanyId &&
                l.ApprovalStatus == LedgerApprovalStatus.Approved &&
                (string.IsNullOrEmpty(q) || l.LedgerName.Contains(q)))
            .OrderBy(l => l.LedgerName)
            .Take(50)
            .Select(l => new { l.LedgerId, l.LedgerName, l.MobileNo, balance = l.ClosingBalance })
            .ToListAsync();

        return Json(parties);
    }

    // Godown auto-assignment (single admin-configured default applied to
    // every item, no per-item picker) was removed — a stale/wrong value
    // there silently attached the wrong Godown to every pushed order.
    // GODOWNNAME in the Tally push is now simply omitted for every item
    // (see TallyService.PushSaleOrderAsync), matching how a manually saved
    // order in Tally behaves when no godown is picked. Kept as a no-op
    // (rather than removing all 6 call sites) as a hook for future
    // per-company dropdown data.
    private Task LoadDropdownsAsync(int companyId) => Task.CompletedTask;

    private async Task<string> GenerateOrderNoAsync(int companyId)
    {
        var prefix = $"SO-{DateTime.Today:yyyyMMdd}-";
        var lastOrder = await _db.SaleOrders
            .Where(o => o.CompanyId == companyId && o.OrderNo.StartsWith(prefix))
            .OrderByDescending(o => o.SaleOrderId)
            .FirstOrDefaultAsync();

        int seq = 1;
        if (lastOrder != null)
        {
            var parts = lastOrder.OrderNo.Split('-');
            if (parts.Length > 0 && int.TryParse(parts[^1], out var last))
                seq = last + 1;
        }

        return $"{prefix}{seq:D3}";
    }

    private async Task LoadTaxSettingsAsync()
    {
        // Only the interstate/intrastate choice is a setting now — the %
        // itself comes from each item's own StockItemTaxSlabs, not a flat
        // admin-entered rate (see ApplyItemTaxAsync/GetEffectiveTaxRatesAsync).
        var taxType = (await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "DefaultTaxType"))?.Value ?? "None";
        ViewBag.DefaultTaxType = taxType;
    }

    private async Task<DateTime?> CalcEditDeadlineAsync(DateTime createdAt)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "OrderEditEnabled");
        if (setting?.Value != "true") return null;

        var cutoffSetting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "OrderEditCutoffHour");
        if (!int.TryParse(cutoffSetting?.Value, out var cutoffHour)) cutoffHour = 19;

        // If created before cutoff → deadline is same day at cutoff; else next day at cutoff
        var cutoffToday = createdAt.Date.AddHours(cutoffHour);
        return createdAt < cutoffToday ? cutoffToday : createdAt.Date.AddDays(1).AddHours(cutoffHour);
    }

    // Picks the latest StockItemTaxSlab whose ApplicableFrom <= asOfDate —
    // Tally's GST% on an item changes over time via rate notifications, so
    // this is the item's actual rate on the order date, not just whatever's
    // "current" today. Returns all-zero if the item has no slab yet (not
    // synced, or genuinely has no GST configured in Tally).
    private async Task<(decimal CGST, decimal SGST, decimal IGST)> GetEffectiveTaxRatesAsync(
        int companyId, int stockItemId, DateTime asOfDate)
    {
        var slab = await _db.StockItemTaxSlabs
            .Where(t => t.StockItemId == stockItemId && t.CompanyId == companyId && t.ApplicableFrom <= asOfDate)
            .OrderByDescending(t => t.ApplicableFrom)
            .FirstOrDefaultAsync();

        return slab == null ? (0, 0, 0) : (slab.CGSTRate, slab.SGSTRate, slab.IGSTRate);
    }

    // Resolves and stamps one line's tax snapshot — which duty heads apply
    // depends on TaxType (IGST for interstate, CGST+SGST for intrastate;
    // still an admin-picked setting, not auto-derived from party/company
    // state), but the % itself always comes from the item's own master data.
    private async Task ApplyItemTaxAsync(SaleOrderItem item, int companyId, string taxType, DateTime orderDate)
    {
        var (cgst, sgst, igst) = await GetEffectiveTaxRatesAsync(companyId, item.StockItemId, orderDate);

        item.CGSTRate = 0; item.SGSTRate = 0; item.IGSTRate = 0;
        item.CGSTAmount = 0; item.SGSTAmount = 0; item.IGSTAmount = 0;

        // AwayFromZero, not .NET's default banker's rounding — Tally rounds
        // each duty head's tax the ordinary way (e.g. 5.225 -> 5.23), and a
        // silent mismatch here (5.225 -> 5.22 under ToEven) is exactly what
        // produced the 2-paisa gap between "As per Transaction" and "As per
        // Calculation" on Tally's own GST Tax Analysis report.
        if (taxType == "IGST")
        {
            item.IGSTRate = igst;
            item.IGSTAmount = Math.Round(item.Amount * igst / 100, 2, MidpointRounding.AwayFromZero);
        }
        else if (taxType == "CGST_SGST")
        {
            item.CGSTRate = cgst;
            item.SGSTRate = sgst;
            item.CGSTAmount = Math.Round(item.Amount * cgst / 100, 2, MidpointRounding.AwayFromZero);
            item.SGSTAmount = Math.Round(item.Amount * sgst / 100, 2, MidpointRounding.AwayFromZero);
        }
    }

    // Order-level totals are always a sum of the (already tax-stamped) line
    // items — never trusted from the client — plus a round-off to the
    // nearest rupee. TaxPercent is kept only as an informational effective
    // rate for display; it no longer drives the calculation.
    private static void ComputeOrderTaxFromItems(SaleOrder order)
    {
        order.CGSTTotal = Math.Round(order.Items.Sum(i => i.CGSTAmount), 2, MidpointRounding.AwayFromZero);
        order.SGSTTotal = Math.Round(order.Items.Sum(i => i.SGSTAmount), 2, MidpointRounding.AwayFromZero);
        order.IGSTTotal = Math.Round(order.Items.Sum(i => i.IGSTAmount), 2, MidpointRounding.AwayFromZero);
        order.TaxTotal  = order.CGSTTotal + order.SGSTTotal + order.IGSTTotal;
        order.TaxPercent = order.TotalAmount > 0 ? Math.Round(order.TaxTotal / order.TotalAmount * 100, 2, MidpointRounding.AwayFromZero) : 0;

        var subtotalPlusTax = order.TotalAmount + order.TaxTotal;
        var roundedGrand = Math.Round(subtotalPlusTax, 0, MidpointRounding.AwayFromZero);
        order.RoundOff   = Math.Round(roundedGrand - subtotalPlusTax, 2, MidpointRounding.AwayFromZero);
        order.GrandTotal = roundedGrand;
    }
}
