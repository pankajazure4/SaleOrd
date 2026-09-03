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

// Single push-cycle timer: pull every pending sale from the Sales API in
// one call, group them by their own CompanyName (falling back to
// AgentConfig.TallyCompanyName for a client whose API has no per-order
// company concept), then push each group into whichever of those companies
// is actually open in Tally right now — same "only touch what's open"
// safety guard SaleOrd.SyncAgent uses, just sourced from the pending-sales
// data instead of a separate companies endpoint (this agent's API contract
// has none). There's no master-sync job here either (unlike
// SaleOrd.SyncAgent) — this agent has no local database to mirror Tally
// masters into, so ledger/stock-item names are just trusted verbatim from
// whatever the API sends.
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
        return await _api.TestConnectionAsync(config);
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
        _logger.Info("Push cycle: checking Tally status...");
        var (reachable, openInTally) = await _tally.CheckStatusAsync(_config.TallyUrl);
        if (!reachable)
        {
            _logger.Warn("Push cycle skipped — Tally unreachable.");
            return;
        }

        _logger.Info("Push cycle: loading pending sales from API...");
        var pending = await _api.GetPendingSalesAsync(_config);
        _logger.Info($"{pending.Count} pending sale(s) loaded.");

        // Group by each invoice's own CompanyName — falls back to the
        // configured TallyCompanyName for a client whose API has no
        // per-order company field (a single-company install).
        var groups = pending
            .GroupBy(p => string.IsNullOrWhiteSpace(p.CompanyName) ? _config.TallyCompanyName : p.CompanyName)
            .ToList();

        // Only touch companies that are actually open in Tally right now —
        // sending a push for a company that isn't the one currently loaded
        // either errors or hangs until the HTTP timeout, every cycle, for no
        // useful result. Same guard SaleOrd.SyncAgent uses.
        var openGroups = groups.Where(g => openInTally.Any(o =>
            string.Equals(Norm(o), Norm(g.Key), StringComparison.OrdinalIgnoreCase))).ToList();

        var skipped = groups.Count - openGroups.Count;
        if (skipped > 0)
            _logger.Warn($"{skipped} company/companies with pending sales are NOT currently open in Tally — skipped this cycle. Open: [{string.Join(", ", openInTally)}]");

        if (openGroups.Count == 0)
        {
            LastPushCycleAt = DateTime.Now;
            NextPushCycleAt = DateTime.Now.AddMinutes(_config.PushIntervalMinutes);
            return;
        }

        int pushed = 0, failed = 0;

        for (int gi = 0; gi < openGroups.Count; gi++)
        {
            var group = openGroups[gi];
            var invoices = group.ToList();
            _logger.Info($"[{group.Key}] Pushing {invoices.Count} sale(s)...");

            for (int i = 0; i < invoices.Count; i++)
            {
                var invoice = invoices[i];
                var voucherType = string.IsNullOrWhiteSpace(invoice.VoucherType) ? _config.VoucherType : invoice.VoucherType;

                // Guard: if this invoice is already in Tally (matched on
                // VoucherTypeName + Reference — see TallyService.
                // VoucherExistsByReferenceAsync), don't push it again. This
                // is what happens when a prior push actually succeeded but
                // the API's own "mark as synced" callback failed, so
                // pending-orders keeps handing back the same invoice —
                // re-pushing would risk a duplicate voucher or a Tally
                // exception. Report success again instead, so the API
                // finally stops sending it.
                if (await _tally.VoucherExistsByReferenceAsync(_config.TallyUrl, group.Key, voucherType, invoice.InvoiceNo))
                {
                    _logger.Warn($"[{group.Key}] {invoice.InvoiceNo} already exists in Tally (matched by Reference) — reporting success without re-pushing.");
                    await _api.ReportPushResultAsync(_config, invoice, true, "Already present in Tally — not re-pushed.");
                    pushed++;

                    if (i < invoices.Count - 1)
                        await Task.Delay(800);
                    continue;
                }

                _logger.Info($"[{group.Key}] Pushing {invoice.InvoiceNo}...");

                var (success, message) = await _tally.PushSalesInvoiceAsync(
                    _config.TallyUrl, invoice, group.Key,
                    _config.VoucherType, _config.BatchName, _config.VoucherClass, _config.PushAsOptional);

                await _api.ReportPushResultAsync(_config, invoice, success, message);

                if (success) { pushed++; _logger.Info($"Pushed {invoice.InvoiceNo} -> Tally"); }
                else { failed++; _logger.Warn($"Push failed for {invoice.InvoiceNo}: {message}"); }

                // Rapid-fire pushes with no spacing crashed Tally's process
                // with a memory access violation on a client install of
                // SaleOrd.SyncAgent — pacing here defensively for the same
                // reason.
                if (i < invoices.Count - 1)
                    await Task.Delay(800);
            }

            if (gi < openGroups.Count - 1)
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
