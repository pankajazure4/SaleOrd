namespace SaleOrd.SyncAgent.Models;

public class Ledger
{
    public int LedgerId { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;

    public string? Address { get; set; }
    public string? State { get; set; }
    public string? PinCode { get; set; }
    public string? MobileNo { get; set; }
    public string? Email { get; set; }
    public string? LedgerFax { get; set; }

    public string? GSTNo { get; set; }
    public string? TaxType { get; set; }
    public string? IncomeTaxNo { get; set; }
    public string? VATTINNo { get; set; }
    public string? FSSAINo { get; set; }

    // Mirrors Models/Domain/Ledger.cs — set by an Admin at Party Approval,
    // pushed to Tally as a UDF field if configured (Settings > Order
    // Defaults > Tally Zone UDF Field), same mechanism as FSSAINo.
    public string? ZoneName { get; set; }

    public decimal CreditLimit { get; set; }
    public string? CreditPeriod { get; set; }     // Tally's "Default credit period", e.g. "1 Days"
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }

    public string? GUID { get; set; }
    public long AlterId { get; set; }

    public int CompanyId { get; set; }
    public DateTime LastSyncedAt { get; set; }

    // Set only by the web app's Masters > Parties "New Party" form — never by
    // this Agent's Tally-fetch upserts. Used to find parties the Agent still
    // owes a Tally push, without touching the bulk of Tally-synced ledgers.
    public bool IsManuallyCreated { get; set; }
    public int ApprovalStatus { get; set; } // 0=Pending, 1=Approved, 2=Rejected — mirrors web app's LedgerApprovalStatus
    public DateTime? TallyPushedAt { get; set; }
}
