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

    public OrderPushJob(IServiceProvider services, ILogger<OrderPushJob> logger, IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _config.GetValue<int>("TallySync:OrderPushIntervalMinutes", 5);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PushPendingOrdersAsync();
            await CheckInvoiceStatusAsync();
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

        var tallyUrl    = Setting("TallyUrl",       "http://localhost:9000");
        var igstLedger  = Setting("TaxLedgerIGST",  "IGST");
        var cgstLedger  = Setting("TaxLedgerCGST",  "CGST");
        var sgstLedger  = Setting("TaxLedgerSGST",  "SGST");

        var pendingOrders = await db.SaleOrders
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Pending)
            .ToListAsync();

        foreach (var order in pendingOrders)
        {
            _logger.LogInformation("Pushing order {OrderNo} to Tally", order.OrderNo);

            var (success, message) = await tallyService.PushSaleOrderAsync(
                tallyUrl, order, igstLedger, cgstLedger, sgstLedger);

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

        bool changed = false;
        foreach (var order in orders)
        {
            if (order.Company == null) continue;
            var tallyUrl = $"http://{order.Company.TallyIp}:{order.Company.TallyPort}";

            var (isInvoiced, invNo, invDate) = await tallyService.CheckInvoiceStatusAsync(
                tallyUrl, order.Company.TallyCompanyName, order.OrderNo);

            if (isInvoiced)
            {
                order.IsInvoiced       = true;
                order.TallyInvoiceNo   = invNo;
                order.TallyInvoiceDate = invDate;
                changed = true;
                _logger.LogInformation("Order {OrderNo} invoiced in Tally as {InvNo}", order.OrderNo, invNo);
            }
        }

        if (changed) await db.SaveChangesAsync();
    }
}
