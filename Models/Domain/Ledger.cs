namespace SaleOrd.Models.Domain;

public class Ledger
{
    public int LedgerId { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;

    // Contact
    public string? Address { get; set; }
    public string? State { get; set; }
    public string? PinCode { get; set; }
    public string? MobileNo { get; set; }
    public string? Email { get; set; }
    public string? LedgerFax { get; set; }

    // GST / Tax
    public string? GSTNo { get; set; }           // PartyGSTIN
    public string? TaxType { get; set; }
    public string? IncomeTaxNo { get; set; }
    public string? VATTINNo { get; set; }
    public string? FSSAINo { get; set; }          // Food License No. — local only, no Tally equivalent
    public string? FSSAIDocumentPath { get; set; } // Relative path under the (non-web-exposed) uploads root; served via a controller action, never a direct static-file URL.

    // Approval workflow (Party Master, created from the web UI) — a party a
    // user creates here isn't usable in Sale Orders until an Admin approves
    // it. Ledgers that arrive via Tally master sync are never gated (they're
    // already real Tally masters), so this defaults to Approved and only
    // MastersController.CreateParty explicitly sets it to Pending; the sync
    // upsert paths never touch this column at all, on purpose.
    public LedgerApprovalStatus ApprovalStatus { get; set; } = LedgerApprovalStatus.Approved;
    public string? RejectionReason { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedById { get; set; }

    // Financial
    public decimal CreditLimit { get; set; }
    public string? CreditPeriod { get; set; }     // Tally's "Default credit period", e.g. "1 Days"
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }

    // Sync tracking
    public string? GUID { get; set; }
    public long AlterId { get; set; }

    public int CompanyId { get; set; }
    public DateTime LastSyncedAt { get; set; }

    public Company? Company { get; set; }
    public AppUser? ReviewedBy { get; set; }
}

public enum LedgerApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
