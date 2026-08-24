namespace SaleOrd.SyncAgent.Models;

// Local-machine agent configuration — stored as JSON under %AppData%\SaleOrdSyncAgent.
// SqlPassword is stored DPAPI-protected (see ConfigService), never in plain text on disk.
public class AgentConfig
{
    public string SqlServer { get; set; } = "";
    public string SqlDatabase { get; set; } = "SaleOrd";
    public string SqlUser { get; set; } = "";
    public string SqlPasswordProtected { get; set; } = "";
    public bool SqlEncrypt { get; set; } = true;

    public string TallyUrl { get; set; } = "http://localhost:9000";

    public int MasterSyncIntervalMinutes { get; set; } = 30;
    public int OrderPushIntervalMinutes { get; set; } = 3;

    // 0 = disabled — rates sync only runs when someone clicks "Sync Rates"
    // manually (the original behaviour, before this setting existed). Not
    // included in the regular master cycle on purpose — see
    // SyncOrchestrator.RunMasterSyncAsync's comment on why the voucher
    // inventory backfill was split out (it's slow enough to starve order
    // push if it shares a timer/lock with anything time-sensitive).
    public int VoucherRatesSyncIntervalMinutes { get; set; } = 0;

    public bool StartMinimized { get; set; } = false;
    public bool AutoStartWithWindows { get; set; } = true;

    // License activation state — see Services/LicenseService.cs. Key is
    // DPAPI-protected the same way SqlPasswordProtected is; the rest is the
    // offline-grace-period snapshot from the last successful portal
    // validation.
    public string LicenseKeyProtected { get; set; } = "";
    public string LicenseLastValidatedOnUtc { get; set; } = "";
    public string LicenseExpiresOnUtc { get; set; } = "";
    public bool LicenseIsTrial { get; set; }
    public string LicenseMachineId { get; set; } = "";
}
