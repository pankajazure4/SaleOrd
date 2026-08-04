namespace SalesPush.SyncAgent.Models;

// Local-machine agent configuration — stored as JSON under %AppData%\SalesPushSyncAgent.
// ApiKey is stored DPAPI-protected (see ConfigService), never in plain text on disk.
//
// Unlike SaleOrd.SyncAgent, this agent has no local SQL Server — the Sales
// API is the only source of pending sales, so the ledger names Tally needs
// for tax/rounding lines live here in config instead of a DB AppSettings
// table.
public class AgentConfig
{
    public string ApiBaseUrl { get; set; } = "";
    public string ApiKeyProtected { get; set; } = "";

    public string TallyUrl { get; set; } = "http://localhost:9000";

    public string SalesLedger { get; set; } = "Sales";
    public string IGSTLedger { get; set; } = "IGST";
    public string CGSTLedger { get; set; } = "CGST";
    public string SGSTLedger { get; set; } = "SGST";
    public string RoundOffLedger { get; set; } = "Round Off";
    public string VoucherType { get; set; } = "Sales";
    public string BatchName { get; set; } = "Primary Batch";

    public int PushIntervalMinutes { get; set; } = 3;

    public bool StartMinimized { get; set; } = false;
    public bool AutoStartWithWindows { get; set; } = true;
}
