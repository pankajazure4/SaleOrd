using SaleOrd.SyncAgent.Models;

namespace SaleOrd.SyncAgent.Services;

public class StatusEventArgs : EventArgs
{
    public bool SqlReachable { get; init; }
    public bool TallyReachable { get; init; }
    public bool IsSyncing { get; init; }
    public DateTime? LastMasterSyncAt { get; init; }
    public DateTime? LastOrderCycleAt { get; init; }
    public DateTime? NextMasterSyncAt { get; init; }
    public DateTime? NextOrderCycleAt { get; init; }
}

// Mirrors MasterSyncJob + OrderPushJob + the manual-sync fold-in already
// built into the web app's SyncController — just re-homed here since this
// agent, not the web app, is the one talking to Tally now (TallySync:Mode
// = Agent). The SQL Server it writes to may be local or remote — the agent
// doesn't distinguish, it just uses whatever connection string is
// configured. Master sync and the order-push/invoice-check cycle run on
// independent timers but share one overlap guard, because they both
// ultimately hit the same Tally instance and must never run concurrently
// (same reasoning as the web app's SyncCoordinator).
public class SyncOrchestrator
{
    private readonly SqlDataService _sql = new();
    private readonly TallyService _tally;
    private readonly VoucherInventorySyncService _voucherSync;
    private readonly AgentLogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private System.Threading.Timer? _masterTimer;
    private System.Threading.Timer? _orderTimer;
    private System.Threading.Timer? _ratesTimer;
    private AgentConfig _config = new();
    private string _connString = "";
    private volatile bool _running;

    public event EventHandler<StatusEventArgs>? StatusChanged;

    public DateTime? LastMasterSyncAt { get; private set; }
    public DateTime? LastOrderCycleAt { get; private set; }
    public DateTime? NextMasterSyncAt { get; private set; }
    public DateTime? NextOrderCycleAt { get; private set; }
    public bool IsRunning => _running;

    public SyncOrchestrator(AgentLogger logger)
    {
        _logger = logger;
        _tally = new TallyService(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, logger);
        _voucherSync = new VoucherInventorySyncService(_sql, _tally, logger);
    }

    public void Start(AgentConfig config)
    {
        _config = config;
        _connString = ConfigService.BuildConnectionString(config);
        _running = true;

        var masterMs = Math.Max(1, config.MasterSyncIntervalMinutes) * 60_000;
        var orderMs  = Math.Max(1, config.OrderPushIntervalMinutes) * 60_000;

        _masterTimer?.Dispose();
        _orderTimer?.Dispose();
        _ratesTimer?.Dispose();
        _ratesTimer = null;

        // Master sync uses the non-blocking skip-if-busy form — if it loses
        // the race to a still-running order cycle, the next scheduled tick
        // (a few minutes away) tries again, so nothing is really lost.
        //
        // Order push does NOT — losing a scheduled push cycle outright
        // (rather than just running it a bit late) means an order can sit
        // unsynced for a full extra interval, which is the actual client-
        // facing symptom ("SO push nahi ho rahe"). blocking:true makes it
        // wait for master sync to finish instead of skipping, so every
        // scheduled order cycle is guaranteed to actually run eventually.
        _masterTimer = new System.Threading.Timer(async _ => await SafeRun(RunMasterSyncAsync), null, 0, masterMs);
        _orderTimer  = new System.Threading.Timer(async _ => await SafeRun(RunOrderCycleAsync, blocking: true), null, 2000, orderMs);

        // Rates sync stays opt-in (0 = off, manual "Sync Rates" button only)
        // — it's the slow one (full historical backfill on first run), so it
        // gets the same non-blocking skip-if-busy treatment as master sync
        // rather than blocking anything time-sensitive.
        if (config.VoucherRatesSyncIntervalMinutes > 0)
        {
            var ratesMs = config.VoucherRatesSyncIntervalMinutes * 60_000;
            _ratesTimer = new System.Threading.Timer(async _ => await SafeRun(RunVoucherInventorySyncAsync), null, 5000, ratesMs);
        }

        NextMasterSyncAt = DateTime.Now.AddMilliseconds(masterMs);
        NextOrderCycleAt = DateTime.Now.AddMilliseconds(2000 + orderMs);
        _logger.Info($"Agent started. Master sync every {config.MasterSyncIntervalMinutes}m, order cycle every {config.OrderPushIntervalMinutes}m, " +
            (config.VoucherRatesSyncIntervalMinutes > 0 ? $"rates sync every {config.VoucherRatesSyncIntervalMinutes}m." : "rates sync manual only."));
        RaiseStatus();
    }

    public void Stop()
    {
        _running = false;
        _masterTimer?.Dispose();
        _orderTimer?.Dispose();
        _ratesTimer?.Dispose();
        _masterTimer = null;
        _orderTimer = null;
        _ratesTimer = null;
        _logger.Info("Agent stopped.");
        RaiseStatus();
    }

    public async Task<(bool Ok, string Message)> TestSqlAsync(AgentConfig config)
    {
        var conn = ConfigService.BuildConnectionString(config);
        return await _sql.TestConnectionAsync(conn);
    }

    public async Task<(bool Ok, string Message)> TestTallyAsync(AgentConfig config)
    {
        var (reachable, companies) = await _tally.CheckStatusAsync(config.TallyUrl);
        return reachable
            ? (true, companies.Count > 0 ? $"Reachable. Open: {string.Join(", ", companies)}" : "Reachable. No company currently open.")
            : (false, "Not reachable");
    }

    // Manual "Sync Now" — runs both cycles back-to-back regardless of timers.
    // Blocking: a user just clicked expecting this to actually run, not
    // silently no-op because a timer tick happened to be mid-cycle.
    public async Task RunNowAsync()
    {
        await SafeRun(async () =>
        {
            await RunMasterSyncAsync();
            await RunOrderCycleAsync();
        }, blocking: true);
    }

    // Manual "Sync Rates" — the voucher-inventory (last-sale-rate) backfill,
    // pulled out of the regular master cycle (see RunMasterSyncAsync's
    // comment) so it can be run on its own schedule/trigger without holding
    // up ledgers/items/godowns or starving order push. Skip-if-busy like
    // master sync — it's an on-demand convenience sync, not something that
    // needs to guarantee running on every click.
    public async Task RunRatesSyncNowAsync()
    {
        await SafeRun(RunVoucherInventorySyncAsync);
    }

    private async Task SafeRun(Func<Task> job, bool blocking = false)
    {
        if (blocking)
        {
            await _lock.WaitAsync();
        }
        else if (!_lock.Wait(0))
        {
            _logger.Info("Skipping cycle — another sync is already in progress.");
            return;
        }
        try
        {
            RaiseStatus(isSyncing: true);
            await job();
        }
        catch (Exception ex)
        {
            _logger.Error("Sync cycle failed unexpectedly", ex);
        }
        finally
        {
            _lock.Release();
            RaiseStatus();
        }
    }

    private async Task RunMasterSyncAsync()
    {
        var companies = await _sql.GetActiveCompaniesAsync(_connString);
        var (reachable, openInTally) = await _tally.CheckStatusAsync(_config.TallyUrl);
        if (!reachable)
        {
            _logger.Warn("Master sync skipped — Tally unreachable.");
            return;
        }

        var toSync = companies.Where(c => openInTally.Any(o =>
            string.Equals(Norm(o), Norm(c.TallyCompanyName), StringComparison.OrdinalIgnoreCase))).ToList();

        if (toSync.Count == 0)
        {
            _logger.Warn($"Master sync skipped — no mapped company currently open in Tally. Open: [{string.Join(", ", openInTally)}]");
            return;
        }

        for (int i = 0; i < toSync.Count; i++)
        {
            var company = toSync[i];
            try
            {
                // Paced, not back-to-back — hammering Tally's HTTP gateway
                // with rapid-fire requests was found to crash Tally itself
                // with a memory access violation on a client install.
                //
                // Logging BEFORE each call (not just after the batch
                // succeeds) is deliberate — a native Tally crash mid-request
                // doesn't throw a normal exception until PostXmlAsync's HTTP
                // call fails, so the last "Fetching X..." line in the Logs
                // tab is what actually pinpoints which specific query did it.
                _logger.Info($"[{company.CompanyName}] Fetching ledgers...");
                var ledgers = await _tally.GetLedgersAsync(_config.TallyUrl, company);
                // Logged immediately, not just folded into the final
                // "Ledgers +X ~Y" summary line — a sudden 0 here (Tally
                // reachable, no exception, just an empty/near-empty response)
                // is itself a red flag worth catching before it gets buried
                // among dozens of other cycles' summary lines, and is exactly
                // the kind of silent failure that doesn't show up as an error.
                if (ledgers.Count == 0)
                    _logger.Warn($"[{company.CompanyName}] Tally returned 0 ledgers — check Tally is actually open on this company and reachable.");
                else
                    _logger.Info($"[{company.CompanyName}] {ledgers.Count} ledger(s) fetched from Tally.");
                await Task.Delay(500);
                _logger.Info($"[{company.CompanyName}] Fetching stock items...");
                var (items, taxSlabRecords) = await _tally.GetStockItemsAsync(_config.TallyUrl, company);
                _logger.Info($"[{company.CompanyName}] {items.Count} stock item(s) fetched from Tally.");
                await Task.Delay(500);
                _logger.Info($"[{company.CompanyName}] Fetching godowns...");
                var godowns = await _tally.GetGodownsAsync(_config.TallyUrl, company);
                _logger.Info($"[{company.CompanyName}] {godowns.Count} godown(s) fetched from Tally.");
                await Task.Delay(500);

                _logger.Info($"[{company.CompanyName}] Saving ledgers to SQL...");
                var (lNew, lUpd) = await _sql.UpsertLedgersAsync(_connString, company.CompanyId, ledgers);
                _logger.Info($"[{company.CompanyName}] Ledgers saved: {lNew} new, {lUpd} updated.");
                _logger.Info($"[{company.CompanyName}] Saving stock items to SQL...");
                var (iNew, iUpd) = await _sql.UpsertStockItemsAsync(_connString, company.CompanyId, items);
                await _sql.UpsertStockItemTaxSlabsAsync(_connString, company.CompanyId, taxSlabRecords);
                _logger.Info($"[{company.CompanyName}] Saving godowns to SQL...");
                var (gNew, gUpd) = await _sql.UpsertGodownsAsync(_connString, company.CompanyId, godowns);

                await _sql.MarkCompanySyncedAsync(_connString, company.CompanyId);

                // Voucher inventory (last-sale-rate history) no longer runs
                // here — see RunVoucherInventorySyncAsync. A full historical
                // backfill on a company with years of vouchers can run for
                // minutes; inline here it was regularly starving order push
                // out of its own scheduled cycle (they share one lock — see
                // Start()) and delaying how fast a newly-created party even
                // got a chance to be picked up by the next master cycle.
                var msg = $"Ledgers +{lNew} ~{lUpd} | Items +{iNew} ~{iUpd} | Godowns +{gNew} ~{gUpd}";
                await _sql.InsertSyncLogAsync(_connString, company.CompanyId, "MasterSync", true, msg);
                _logger.Info($"[{company.CompanyName}] {msg}");
            }
            catch (Exception ex)
            {
                await _sql.InsertSyncLogAsync(_connString, company.CompanyId, "MasterSync", false, ex.Message);
                _logger.Error($"Master sync failed for {company.CompanyName}", ex);
            }

            if (i < toSync.Count - 1)
                await Task.Delay(800);
        }

        LastMasterSyncAt = DateTime.Now;
        NextMasterSyncAt = DateTime.Now.AddMinutes(_config.MasterSyncIntervalMinutes);
    }

    // Item rate history (VoucherInventoryEntries) for the last-sale-rate
    // convenience feature — split out from RunMasterSyncAsync so a long
    // first-time historical backfill can't hold the shared lock and delay
    // ledgers/items/godowns or order push. Triggered from FrmMain's
    // "Sync Rates" button, and optionally also on its own timer if
    // VoucherRatesSyncIntervalMinutes > 0 (0 = manual only, the default).
    private async Task RunVoucherInventorySyncAsync()
    {
        var companies = await _sql.GetActiveCompaniesAsync(_connString);
        var (reachable, openInTally) = await _tally.CheckStatusAsync(_config.TallyUrl);
        if (!reachable)
        {
            _logger.Warn("Rate sync skipped — Tally unreachable.");
            return;
        }

        var toSync = companies.Where(c => openInTally.Any(o =>
            string.Equals(Norm(o), Norm(c.TallyCompanyName), StringComparison.OrdinalIgnoreCase))).ToList();

        if (toSync.Count == 0)
        {
            _logger.Warn($"Rate sync skipped — no mapped company currently open in Tally. Open: [{string.Join(", ", openInTally)}]");
            return;
        }

        for (int i = 0; i < toSync.Count; i++)
        {
            var company = toSync[i];
            try
            {
                _logger.Info($"[{company.CompanyName}] Syncing voucher inventory ({(company.LastVoucherAlterId == null ? "full history" : "incremental")})...");
                var (vNew, vUpd, vMode) = await _voucherSync.SyncCompanyAsync(_connString, _config.TallyUrl, company);

                var msg = $"Vouchers +{vNew} ~{vUpd} ({vMode})";
                await _sql.InsertSyncLogAsync(_connString, company.CompanyId, "RateSync", true, msg);
                _logger.Info($"[{company.CompanyName}] {msg}");
            }
            catch (Exception ex)
            {
                await _sql.InsertSyncLogAsync(_connString, company.CompanyId, "RateSync", false, ex.Message);
                _logger.Error($"Rate sync failed for {company.CompanyName}", ex);
            }

            if (i < toSync.Count - 1)
                await Task.Delay(800);
        }
    }

    private async Task RunOrderCycleAsync()
    {
        // Logging BEFORE each step — this method previously had zero log
        // output until the first company-level "Pushing order..."/"Checking
        // invoice status..." line, which meant a hang anywhere in these
        // earlier SQL/Tally calls was completely invisible in the Logs tab.
        _logger.Info("Order cycle: loading active companies from SQL...");
        var allCompanies = await _sql.GetActiveCompaniesAsync(_connString);

        // Only touch companies that are actually open in Tally right now —
        // mirrors the same check RunMasterSyncAsync already does. Without
        // this, a stale/deprecated DB company row (IsActive=1 but not the
        // company currently loaded in Tally) still gets an invoice-check
        // query sent for it, which either errors or — as seen on a client
        // install — hangs until the 30s HttpClient timeout, every cycle,
        // for no useful result.
        _logger.Info("Order cycle: checking Tally status...");
        var (reachable, openInTally) = await _tally.CheckStatusAsync(_config.TallyUrl);
        if (!reachable)
        {
            _logger.Warn("Order cycle skipped — Tally unreachable.");
            return;
        }
        var companies = allCompanies.Where(c => openInTally.Any(o =>
            string.Equals(Norm(o), Norm(c.TallyCompanyName), StringComparison.OrdinalIgnoreCase))).ToList();

        _logger.Info("Order cycle: loading app settings from SQL...");
        var settings = await _sql.GetAppSettingsAsync(_connString);
        string Setting(string key, string def) => settings.TryGetValue(key, out var v) ? v : def;

        var igstLedger     = Setting("TaxLedgerIGST", "IGST");
        var cgstLedger     = Setting("TaxLedgerCGST", "CGST");
        var sgstLedger     = Setting("TaxLedgerSGST", "SGST");
        var salesLedger    = Setting("SalesLedger", "Sales");
        var roundOffLedger = Setting("TaxLedgerRoundOff", "Round Off");
        var voucherType    = Setting("DefaultVoucherType", "Sales Order");
        var fssaiUdfField  = settings.TryGetValue("TallyFssaiUdfField", out var fu) ? fu : null;

        int pushed = 0, failed = 0, invoiced = 0, partiesPushed = 0, partiesFailed = 0;
        var companyList = companies.ToList();
        _logger.Info($"Order cycle: {companyList.Count} compan{(companyList.Count == 1 ? "y" : "ies")} to process.");

        for (int ci = 0; ci < companyList.Count; ci++)
        {
            var company = companyList[ci];
            _logger.Info($"[{company.CompanyName}] Loading pending orders from SQL...");
            var pending = await _sql.GetPendingOrdersAsync(_connString, company.CompanyId);
            _logger.Info($"[{company.CompanyName}] {pending.Count} pending order(s) loaded.");
            for (int i = 0; i < pending.Count; i++)
            {
                var order = pending[i];
                var isAlter = !string.IsNullOrEmpty(order.TallyVoucherNo);
                _logger.Info($"[{company.CompanyName}] Pushing order {order.OrderNo} ({(isAlter ? "Alter" : "Create")})...");
                var (success, message) = await _tally.PushSaleOrderAsync(
                    _config.TallyUrl, order, company.TallyCompanyName,
                    salesLedger, igstLedger, cgstLedger, sgstLedger,
                    roundOffLedger, isAlter, voucherType);

                await _sql.UpdateOrderPushResultAsync(_connString, order.SaleOrderId, success,
                    success ? order.OrderNo : null, message);
                await _sql.InsertSyncLogAsync(_connString, company.CompanyId, "OrderPush", success,
                    $"Order {order.OrderNo}: {message}");

                if (success) { pushed++; _logger.Info($"Pushed {order.OrderNo} -> Tally ({(isAlter ? "Alter" : "Create")})"); }
                else { failed++; _logger.Warn($"Push failed for {order.OrderNo}: {message}"); }

                // Rapid-fire pushes with no spacing were implicated in
                // crashing Tally's process with a memory access violation.
                if (i < pending.Count - 1)
                    await Task.Delay(800);
            }

            // Manually-created parties approved in Admin > Pending Parties —
            // the web app only ever flips ApprovalStatus there (it has no
            // direct route to this client's Tally instance), so this Agent
            // is the one that actually creates them as Tally ledger masters.
            // TallyPushedAt IS NULL is what marks "approved, still owed a push".
            _logger.Info($"[{company.CompanyName}] Loading approved parties awaiting Tally push...");
            var pendingParties = await _sql.GetApprovedUnpushedManualPartiesAsync(_connString, company.CompanyId);
            if (pendingParties.Count > 0)
            {
                _logger.Info($"[{company.CompanyName}] {pendingParties.Count} approved part{(pendingParties.Count == 1 ? "y" : "ies")} awaiting Tally push.");
                foreach (var party in pendingParties)
                {
                    var (success, message) = await _tally.PushLedgerAsync(
                        _config.TallyUrl, party, company.TallyCompanyName, fssaiUdfField);

                    await _sql.InsertSyncLogAsync(_connString, company.CompanyId, "PartyApproval", success,
                        $"Party \"{party.LedgerName}\" (LedgerId {party.LedgerId}): {message}");

                    if (success)
                    {
                        await _sql.MarkLedgerPushedToTallyAsync(_connString, party.LedgerId);
                        partiesPushed++;
                        _logger.Info($"Pushed party \"{party.LedgerName}\" -> Tally");
                    }
                    else
                    {
                        partiesFailed++;
                        _logger.Warn($"Party push failed for \"{party.LedgerName}\": {message}");
                    }

                    await Task.Delay(800);
                }
            }

            _logger.Info($"[{company.CompanyName}] Loading uninvoiced synced orders from SQL...");
            var uninvoiced = await _sql.GetUninvoicedSyncedOrdersAsync(_connString, company.CompanyId);
            _logger.Info($"[{company.CompanyName}] {uninvoiced.Count} uninvoiced order(s) loaded.");
            if (uninvoiced.Count > 0)
            {
                // One Tally round-trip for this whole batch, not one per
                // order — see GetRecentSalesInvoicesAsync for why that matters.
                var fromDate = uninvoiced.Min(o => o.OrderDate);
                _logger.Info($"[{company.CompanyName}] Checking invoice status for {uninvoiced.Count} order(s), from {fromDate:dd-MMM-yyyy}...");

                // Must match every Sales-abbreviation voucher type, not just
                // the one literally named "Sales" — this company has several
                // (see the "Sales voucher types matched" line from master
                // sync). Missing this previously meant an invoice raised
                // under any other Sales-type name never matched here, so its
                // order's Invoiced status silently never got set.
                var salesTypes = await _tally.GetSalesVoucherTypeNamesAsync(_config.TallyUrl, company.TallyCompanyName);
                var invoices = await _tally.GetRecentSalesInvoicesAsync(_config.TallyUrl, company.TallyCompanyName, fromDate, salesTypes);
                _logger.Info($"[{company.CompanyName}] {invoices.Count} Sales voucher(s) fetched from Tally ({string.Join(", ", salesTypes)}) for matching.");

                int matchedThisCompany = 0;
                foreach (var order in uninvoiced)
                {
                    if (TallyService.TryMatchInvoice(invoices, order.OrderNo, out var invNo, out var invDate))
                    {
                        await _sql.UpdateOrderInvoiceStatusAsync(_connString, order.SaleOrderId, invNo, invDate);
                        invoiced++;
                        matchedThisCompany++;
                        _logger.Info($"Order {order.OrderNo} invoiced in Tally as {invNo}");
                    }
                }

                // If invoices.Count is 0 (or clearly too low) while orders
                // stay unmatched cycle after cycle, that points at the Tally
                // query itself (wrong company, permissions, voucher types);
                // if invoices.Count looks right but orders still don't
                // match, the invoice was likely raised without linking it to
                // the Sales Order in Tally (REFERENCE/order-list only
                // auto-fills when raised "against" the order).
                var unmatched = uninvoiced.Count - matchedThisCompany;
                if (unmatched > 0)
                    _logger.Info($"[{company.CompanyName}] {unmatched} order(s) still not matched to any fetched Sales voucher.");
            }

            if (ci < companyList.Count - 1)
                await Task.Delay(800);
        }

        if (pushed > 0 || failed > 0 || invoiced > 0 || partiesPushed > 0 || partiesFailed > 0)
            _logger.Info($"Order cycle: {pushed} pushed, {failed} failed, {invoiced} newly invoiced, {partiesPushed} part{(partiesPushed == 1 ? "y" : "ies")} pushed, {partiesFailed} part{(partiesFailed == 1 ? "y" : "ies")} failed.");

        LastOrderCycleAt = DateTime.Now;
        NextOrderCycleAt = DateTime.Now.AddMinutes(_config.OrderPushIntervalMinutes);
    }

    private static string Norm(string? v) => (v ?? "").Trim();

    private void RaiseStatus(bool isSyncing = false)
    {
        StatusChanged?.Invoke(this, new StatusEventArgs
        {
            IsSyncing = isSyncing,
            LastMasterSyncAt = LastMasterSyncAt,
            LastOrderCycleAt = LastOrderCycleAt,
            NextMasterSyncAt = NextMasterSyncAt,
            NextOrderCycleAt = NextOrderCycleAt
        });
    }
}
