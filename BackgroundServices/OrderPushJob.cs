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

        var pendingOrders = await db.SaleOrders
            .Include(o => o.Items)
            .Include(o => o.Company)
            .Include(o => o.Ledger)
            .Where(o => o.Status == OrderStatus.Pending)
            .ToListAsync();

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

        // Check up to 50 synced-but-not-invoiced orders per cycle
        var orders = await db.SaleOrders
            .Include(o => o.Company)
            .Where(o => o.Status == OrderStatus.Synced && !o.IsInvoiced)
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
