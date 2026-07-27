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

    public bool StartMinimized { get; set; } = false;
    public bool AutoStartWithWindows { get; set; } = true;
}
