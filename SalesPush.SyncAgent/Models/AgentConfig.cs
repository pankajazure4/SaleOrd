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

    // "Authorization"/"Bearer" by default. Not every client's API agrees on
    // that convention — e.g. Siena Bathroom wants a raw "X-Api-Key: <key>"
    // header instead — so both are configurable per install rather than
    // hardcoded. Leave AuthScheme blank to send the key as-is with no
    // scheme prefix.
    public string AuthHeaderName { get; set; } = "Authorization";
    public string AuthScheme { get; set; } = "Bearer";

    public string TallyUrl { get; set; } = "http://localhost:9000";

    // Fallbacks only — the real values normally come from the API per
    // invoice/item (SaleInvoice.CompanyName/VoucherType,
    // SaleInvoiceItem.BatchName). Used when a client's API leaves one out,
    // e.g. a single-company install with no per-order company field.
    public string TallyCompanyName { get; set; } = "";
    public string VoucherType { get; set; } = "Sales";
    public string BatchName { get; set; } = "Primary Batch";
    public string VoucherClass { get; set; } = "";

    // Every pushed voucher is ISOPTIONAL=Yes by default — it lands in Tally
    // without hitting the books until an accountant confirms it. Kept as a
    // switch (not hardcoded) in case a client later wants regular vouchers.
    public bool PushAsOptional { get; set; } = true;

    public int PushIntervalMinutes { get; set; } = 3;

    public bool StartMinimized { get; set; } = false;
    public bool AutoStartWithWindows { get; set; } = true;

    // Groundwork for a later feature: pulling a Tally Collection report
    // (e.g. StockItemAggr_By) and pushing/altering it into a SQL table on
    // every sync cycle. Not wired to any sync logic yet — just a place for
    // the connection details to live once that part is built. Kept as
    // separate fields (not one connection string) so the Config UI can offer
    // a proper Server/Database/UID/Password form with a Test Connection
    // button; only the password is sensitive enough to DPAPI-protect.
    public string SqlServer { get; set; } = "";
    public string SqlDatabase { get; set; } = "";
    public string SqlUserId { get; set; } = "";
    public string SqlPasswordProtected { get; set; } = "";

    // License activation state — see Services/LicenseService.cs. Key is
    // DPAPI-protected like the API key; the rest is the offline-grace-period
    // snapshot from the last successful portal validation.
    public string LicenseKeyProtected { get; set; } = "";
    public string LicenseLastValidatedOnUtc { get; set; } = "";
    public string LicenseExpiresOnUtc { get; set; } = "";
    public bool LicenseIsTrial { get; set; }
    public string LicenseMachineId { get; set; } = "";
}
