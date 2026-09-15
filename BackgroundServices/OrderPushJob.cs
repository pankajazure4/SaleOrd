using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.BackgroundServices;

public class OrderPushJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OrderPushJob> _logger;
    private readonly IConfiguration _config;
    private readonly SyncCoordinator _coordinator;

    public OrderPushJob(IServiceProvider services, ILogger<OrderPushJob> logger, IConfiguration config, SyncCoordinator coordinator)
    {
        _services = services;
        _logger = logger;
        _config = config;
        _coordinator = coordinator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (TallySyncMode.IsAgentManaged(_config))
        {
            _logger.LogInformation("TallySync:Mode is Agent — order push/invoice check runs from SaleOrd.SyncAgent instead. Background job disabled.");
            return;
        }

        var intervalMinutes = _config.GetValue<int>("TallySync:OrderPushIntervalMinutes", 3);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Blocking, not TryStart — waits for a still-running master sync
            // to finish rather than skipping this cycle outright. See
            // SyncCoordinator for why order push specifically needs this.
            await _coordinator.StartAsync();
            try
            {
                await PushPendingOrdersAsync();
                await CheckInvoiceStatusAsync();
            }
            finally
            {
                _coordinator.Finish();
            }
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task PushPendingOrdersAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tallyService = scope.ServiceProvider.GetRequiredService<TallyService>();

        var settings = await db.AppSettings.ToListAsync();
        string Setting(string key, string def) =>
            settings.FirstOrDefault(s => s.Key == key)?.Value ?? def;

        var tallyUrl      = Setting("TallyUrl",           "http://localhost:9000");
        var igstLedger    = Setting("TaxLedgerIGST",       "IGST");
        var cgstLedger    = Setting("TaxLedgerCGST",       "CGST");
        var sgstLedger    = Setting("TaxLedgerSGST",       "SGST");
        var salesLedger   = Setting("SalesLedger",         "Sales");
        var roundOffLedger= Setting("TaxLedgerRoundOff",   "Round Off");
        var voucherType   = Setting("DefaultVoucherType",  "Sales Order");

        var allPendingOrders = await db.SaleOrders
            .Include(o => o.Items)
            .Include(o => o.Company)
            .Include(o => o.Ledger)
            .Where(o => o.Status == OrderStatus.Pending)
            .ToListAsync();

        if (!allPendingOrders.Any()) return;

        // Only push orders whose company is actually open in Tally right
        // now — a company that's IsActive in the DB but not currently loaded
        // in Tally would otherwise still get a push attempt every cycle,
        // which either errors immediately or hangs until the HttpClient
        // timeout for no useful result (seen on a client install with a
        // stale/deprecated company row still marked active).
        var (reachable, openInTally) = await tallyService.CheckStatusAsync(tallyUrl);
        if (!reachable)
        {
            _logger.LogWarning("Order push skipped — Tally unreachable at {Url}.", tallyUrl);
            return;
        }
        // Both conditions, explicitly: the company row must be IsActive in
        // the DB AND its Tally name must match one currently open in Tally.
        // Matching only one of the two isn't enough — a deactivated company
        // could still coincidentally share a name with something open, and
        // an active company that Tally doesn't have loaded is the exact
        // hang/timeout scenario this whole check exists to avoid.
        var pendingOrders = allPendingOrders
            .Where(o => o.Company != null && o.Company.IsActive &&
                openInTally.Any(open => string.Equals(
                    open.Trim(), o.Company.TallyCompanyName.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        for (int i = 0; i < pendingOrders.Count; i++)
        {
            var order = pendingOrders[i];
            var companyName = order.Company?.TallyCompanyName ?? string.Empty;
            var isAlter     = !string.IsNullOrEmpty(order.TallyVoucherNo);
            _logger.LogInformation("Pushing order {OrderNo} to Tally ({Action})", order.OrderNo, isAlter ? "Alter" : "Create");

            var (success, message) = await tallyService.PushSaleOrderAsync(
                tallyUrl, order, companyName,
                salesLedger, igstLedger, cgstLedger, sgstLedger,
                roundOffLedger, isAlter, voucherType);

            order.Status = success ? OrderStatus.Synced : OrderStatus.Error;
            order.SyncedAt = DateTime.Now;
            order.TallyVoucherNo = success ? order.OrderNo : null;
            order.SyncError = success ? null : message;

            db.SyncLogs.Add(new SyncLog
            {
                CompanyId = order.CompanyId,
                SyncType = "OrderPush",
                IsSuccess = success,
                Message = $"Order {order.OrderNo}: {message}"
            });

            // Tally's HTTP gateway is fragile under back-to-back requests —
            // firing every pending order's Import Data XML in a tight loop
            // with zero spacing was implicated (alongside the unbounded
            // invoice-check scans) in crashing Tally's native process with a
            // memory access violation. A short pause between pushes gives it
            // room to breathe.
            if (i < pendingOrders.Count - 1)
                await Task.Delay(800);
        }

        if (pendingOrders.Any())
            await db.SaveChangesAsync();

        // Orders cancelled after already reaching Tally (TallyVoucherNo set)
        // still owe Tally an ACTION="Cancel" push — see CancelPushPending on
        // the model. Separate query from the Pending loop above on purpose:
        // cancelling flips Status to Cancelled immediately in the app, so
        // these never show up in the Status=1/Pending query at all.
        // (Mirrored from SaleOrd.SyncAgent's SyncOrchestrator.)
        var allCancelPending = await db.SaleOrders
            .Include(o => o.Items)
            .Include(o => o.Company)
            .Include(o => o.Ledger)
            .Where(o => o.Status == OrderStatus.Cancelled && o.CancelPushPending)
            .ToListAsync();

        var cancelPending = allCancelPending
            .Where(o => o.Company != null && o.Company.IsActive &&
                openInTally.Any(open => string.Equals(
                    open.Trim(), o.Company.TallyCompanyName.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        for (int i = 0; i < cancelPending.Count; i++)
        {
            var order = cancelPending[i];
            var companyName = order.Company?.TallyCompanyName ?? string.Empty;
            _logger.LogInformation("Cancelling order {OrderNo} in Tally", order.OrderNo);

            var (success, message) = await tallyService.PushSaleOrderAsync(
                tallyUrl, order, companyName,
                salesLedger, igstLedger, cgstLedger, sgstLedger,
                roundOffLedger, isAlter: true, voucherType, isCancel: true);

            order.CancelPushPending = !success;
            order.CancelPushedAt = success ? DateTime.Now : null;
            order.SyncError = success ? null : message;

            db.SyncLogs.Add(new SyncLog
            {
                CompanyId = order.CompanyId,
                SyncType = "OrderCancelPush",
                IsSuccess = success,
                Message = $"Order {order.OrderNo}: {message}"
            });

            if (i < cancelPending.Count - 1)
                await Task.Delay(800);
        }

        if (cancelPending.Any())
            await db.SaveChangesAsync();
    }

    private async Task CheckInvoiceStatusAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tallyService = scope.ServiceProvider.GetRequiredService<TallyService>();

        // No Take(N) here on purpose — this used to cap at 50, ORDER BY
        // SaleOrderId, no rotation, which meant every cycle re-checked the
        // SAME oldest 50 uninvoiced orders. If any of those never actually
        // match, the queue never advances to newer ones — confirmed on the
        // live Agent (its mirror of this same query) where nothing past a
        // specific date ever got checked at all. Scoped to active companies
        // only; the openInTally check per company group below is the second
        // half of the "IsActive AND open in Tally" pair.
        var orders = await db.SaleOrders
            .Include(o => o.Company)
            .Where(o => o.Status == OrderStatus.Synced && !o.IsInvoiced && o.Company != null && o.Company.IsActive)
            .OrderBy(o => o.SaleOrderId)
            .ToListAsync();

        if (!orders.Any()) return;

        bool changed = false;
        // One Tally round-trip per company for this whole batch, not one per
        // order — see GetRecentSalesInvoicesAsync for why that mattered.
        var groups = orders.Where(o => o.Company != null).GroupBy(o => o.Company!).ToList();
        for (int g = 0; g < groups.Count; g++)
        {
            var company = groups[g].Key;
            var tallyUrl = $"http://{company.TallyIp}:{company.TallyPort}";

            // Skip companies that aren't currently open in Tally — same
            // reasoning as PushPendingOrdersAsync above.
            var (reachable, openInTally) = await tallyService.CheckStatusAsync(tallyUrl);
            if (!reachable || !openInTally.Any(open =>
                    string.Equals(open.Trim(), company.TallyCompanyName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("[{Company}] Skipping invoice check — not currently open in Tally.", company.TallyCompanyName);
                continue;
            }

            var fromDate = groups[g].Min(o => o.OrderDate);

            _logger.LogInformation("[{Company}] Checking invoice status for {Count} order(s), from {FromDate:dd-MMM-yyyy}...",
                company.TallyCompanyName, groups[g].Count(), fromDate);
            var salesTypes = await tallyService.GetSalesVoucherTypeNamesAsync(tallyUrl, company.TallyCompanyName);
            var invoices = await tallyService.GetRecentSalesInvoicesAsync(tallyUrl, company.TallyCompanyName, fromDate, salesTypes);

            var unmatchedOrderNos = new List<string>();
            foreach (var order in groups[g])
            {
                if (TallyService.TryMatchInvoice(invoices, order.OrderNo, out var invNo, out var invDate))
                {
                    order.IsInvoiced       = true;
                    order.TallyInvoiceNo   = invNo;
                    order.TallyInvoiceDate = invDate;
                    changed = true;
                    _logger.LogInformation("Order {OrderNo} invoiced in Tally as {InvNo}", order.OrderNo, invNo);
                }
                else
                {
                    unmatchedOrderNos.Add(order.OrderNo);
                }
            }

            // Quoted, side-by-side dump of what our own OrderNo strings look
            // like vs what Tally's vouchers actually carry as order refs — a
            // real mismatch (stray whitespace, case, an extra character) is
            // directly visible here without another manual Postman round.
            if (unmatchedOrderNos.Count > 0)
            {
                var sampleOrders = string.Join(", ", unmatchedOrderNos.Take(10).Select(o => $"'{o}'"));
                _logger.LogWarning("{Count} order(s) NOT matched — e.g. {Sample}{More}",
                    unmatchedOrderNos.Count, sampleOrders, unmatchedOrderNos.Count > 10 ? $" (+{unmatchedOrderNos.Count - 10} more)" : "");

                var allOrderRefs = invoices.SelectMany(inv => inv.OrderRefs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var sampleRefs = string.Join(", ", allOrderRefs.Take(10).Select(r => $"'{r}'"));
                _logger.LogInformation("{Count} distinct order-ref(s) found across {VoucherCount} fetched voucher(s) — e.g. {Sample}{More}",
                    allOrderRefs.Count, invoices.Count, sampleRefs, allOrderRefs.Count > 10 ? $" (+{allOrderRefs.Count - 10} more)" : "");
            }

            if (g < groups.Count - 1)
                await Task.Delay(800);
        }

        if (changed) await db.SaveChangesAsync();
    }
}
