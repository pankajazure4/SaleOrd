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
            if (_coordinator.TryStart())
            {
                try
                {
                    await PushPendingOrdersAsync();
                    await CheckInvoiceStatusAsync();
                }
                finally
                {
                    _coordinator.Finish();
                }
            }
            else
            {
                _logger.LogInformation("Skipping order push cycle — another sync is already in progress.");
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
        var batchName     = Setting("DefaultBatchName",    "Primary Batch");

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
                roundOffLedger, isAlter, voucherType, batchName);

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
    }

    private async Task CheckInvoiceStatusAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tallyService = scope.ServiceProvider.GetRequiredService<TallyService>();

        // Check up to 50 synced-but-not-invoiced orders per cycle — scoped to
        // active companies only; the openInTally check per company group
        // below is the second half of the "IsActive AND open in Tally" pair.
        var orders = await db.SaleOrders
            .Include(o => o.Company)
            .Where(o => o.Status == OrderStatus.Synced && !o.IsInvoiced && o.Company != null && o.Company.IsActive)
            .OrderBy(o => o.SaleOrderId)
            .Take(50)
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
            var invoices = await tallyService.GetRecentSalesInvoicesAsync(tallyUrl, company.TallyCompanyName, fromDate);

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
            }

            if (g < groups.Count - 1)
                await Task.Delay(800);
        }

        if (changed) await db.SaveChangesAsync();
    }
}
