using SalesPush.SyncAgent.Models;

namespace SalesPush.SyncAgent.Services;

public class StatusEventArgs : EventArgs
{
    public bool ApiReachable { get; init; }
    public bool TallyReachable { get; init; }
    public bool IsSyncing { get; init; }
    public DateTime? LastPushCycleAt { get; init; }
    public DateTime? NextPushCycleAt { get; init; }
}

// Single push-cycle timer: load active companies from the Sales API, check
// which of those are actually open in Tally right now, then for each one
// pull its pending sales and push them in as Sales Invoice vouchers,
// reporting each result back to the API. There's no master-sync job here
// (unlike SaleOrd.SyncAgent) — this agent has no local database to mirror
// Tally masters into, so ledger/stock-item names are just trusted verbatim
// from whatever the API sends.
public class SyncOrchestrator
{
    private readonly ApiDataService _api;
    private readonly TallyService _tally;
    private readonly AgentLogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private System.Threading.Timer? _pushTimer;
    private AgentConfig _config = new();
    private volatile bool _running;

    public event EventHandler<StatusEventArgs>? StatusChanged;

    public DateTime? LastPushCycleAt { get; private set; }
    public DateTime? NextPushCycleAt { get; private set; }
    public bool IsRunning => _running;

    public SyncOrchestrator(AgentLogger logger)
    {
        _logger = logger;
        _api = new ApiDataService(logger);
        _tally = new TallyService(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, logger);
    }

    public void Start(AgentConfig config)
    {
        _config = config;
        _running = true;

        var pushMs = Math.Max(1, config.PushIntervalMinutes) * 60_000;

        _pushTimer?.Dispose();
        _pushTimer = new System.Threading.Timer(async _ => await SafeRun(RunPushCycleAsync), null, 0, pushMs);

        NextPushCycleAt = DateTime.Now.AddMilliseconds(pushMs);
        _logger.Info($"Agent started. Push cycle every {config.PushIntervalMinutes}m.");
        RaiseStatus();
    }

    public void Stop()
    {
        _running = false;
        _pushTimer?.Dispose();
        _pushTimer = null;
        _logger.Info("Agent stopped.");
        RaiseStatus();
    }

    public async Task<(bool Ok, string Message)> TestApiAsync(AgentConfig config)
    {
        return await _api.TestConnectionAsync(config.ApiBaseUrl, ConfigService.Unprotect(config.ApiKeyProtected));
    }

    public async Task<(bool Ok, string Message)> TestTallyAsync(AgentConfig config)
    {
        var (reachable, companies) = await _tally.CheckStatusAsync(config.TallyUrl);
        return reachable
            ? (true, companies.Count > 0 ? $"Reachable. Open: {string.Join(", ", companies)}" : "Reachable. No company currently open.")
            : (false, "Not reachable");
    }

    // Manual "Push Now" — runs the cycle regardless of the timer, respecting
    // the same overlap guard so it can't collide with a timer tick.
    public async Task RunNowAsync()
    {
        await SafeRun(RunPushCycleAsync);
    }

    private async Task SafeRun(Func<Task> job)
    {
        if (!_lock.Wait(0))
        {
            _logger.Info("Skipping cycle — another push is already in progress.");
            return;
        }
        try
        {
            RaiseStatus(isSyncing: true);
            await job();
        }
        catch (Exception ex)
        {
            _logger.Error("Push cycle failed unexpectedly", ex);
        }
        finally
        {
            _lock.Release();
            RaiseStatus();
        }
    }

    private async Task RunPushCycleAsync()
    {
        var apiKey = ConfigService.Unprotect(_config.ApiKeyProtected);

        _logger.Info("Push cycle: loading active companies from API...");
        var allCompanies = await _api.GetActiveCompaniesAsync(_config.ApiBaseUrl, apiKey);

        _logger.Info("Push cycle: checking Tally status...");
        var (reachable, openInTally) = await _tally.CheckStatusAsync(_config.TallyUrl);
        if (!reachable)
        {
            _logger.Warn("Push cycle skipped — Tally unreachable.");
            return;
        }

        // Only touch companies that are actually open in Tally right now —
        // sending a push for a company that isn't the one currently loaded
        // either errors or hangs until the HTTP timeout, every cycle, for no
        // useful result. Same guard SaleOrd.SyncAgent uses.
        var companies = allCompanies.Where(c => openInTally.Any(o =>
            string.Equals(Norm(o), Norm(c.TallyCompanyName), StringComparison.OrdinalIgnoreCase))).ToList();

        if (companies.Count == 0)
        {
            _logger.Warn($"Push cycle skipped — no mapped company currently open in Tally. Open: [{string.Join(", ", openInTally)}]");
            return;
        }

        int pushed = 0, failed = 0;

        for (int ci = 0; ci < companies.Count; ci++)
        {
            var company = companies[ci];
            _logger.Info($"[{company.CompanyName}] Loading pending sales from API...");
            var pending = await _api.GetPendingSalesAsync(_config.ApiBaseUrl, apiKey, company.CompanyId);
            _logger.Info($"[{company.CompanyName}] {pending.Count} pending sale(s) loaded.");

            for (int i = 0; i < pending.Count; i++)
            {
                var invoice = pending[i];
                var isAlter = !string.IsNullOrEmpty(invoice.TallyVoucherNo);
                _logger.Info($"[{company.CompanyName}] Pushing {invoice.InvoiceNo} ({(isAlter ? "Alter" : "Create")})...");

                var (success, message) = await _tally.PushSalesInvoiceAsync(
                    _config.TallyUrl, invoice, company.TallyCompanyName,
                    _config.SalesLedger, _config.IGSTLedger, _config.CGSTLedger, _config.SGSTLedger,
                    _config.RoundOffLedger, isAlter, _config.VoucherType, _config.BatchName);

                await _api.ReportPushResultAsync(_config.ApiBaseUrl, apiKey, invoice.SaleInvoiceId,
                    success, success ? invoice.InvoiceNo : null, message);

                if (success) { pushed++; _logger.Info($"Pushed {invoice.InvoiceNo} -> Tally ({(isAlter ? "Alter" : "Create")})"); }
                else { failed++; _logger.Warn($"Push failed for {invoice.InvoiceNo}: {message}"); }

                // Rapid-fire pushes with no spacing crashed Tally's process
                // with a memory access violation on a client install of
                // SaleOrd.SyncAgent — pacing here defensively for the same
                // reason.
                if (i < pending.Count - 1)
                    await Task.Delay(800);
            }

            if (ci < companies.Count - 1)
                await Task.Delay(800);
        }

        if (pushed > 0 || failed > 0)
            _logger.Info($"Push cycle: {pushed} pushed, {failed} failed.");

        LastPushCycleAt = DateTime.Now;
        NextPushCycleAt = DateTime.Now.AddMinutes(_config.PushIntervalMinutes);
    }

    private static string Norm(string? v) => (v ?? "").Trim();

    private void RaiseStatus(bool isSyncing = false)
    {
        StatusChanged?.Invoke(this, new StatusEventArgs
        {
            IsSyncing = isSyncing,
            LastPushCycleAt = LastPushCycleAt,
            NextPushCycleAt = NextPushCycleAt
        });
    }
}
