namespace SaleOrd.Models.Domain;

public class Ledger
{
    public int LedgerId { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;

    // Optional — local only, no Tally equivalent, same as FSSAINo below.
    // Not required at creation (Masters > Parties).
    public string? OutletName { get; set; }

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

    // True only for parties created via Masters > Parties (set once, at
    // creation, by MastersController.CreateParty) — never set by Tally master
    // sync, so it's the one reliable way to tell "created here" apart from
    // the bulk of ledgers that arrived from Tally. Admin/PendingParties uses
    // this to show only manually-created parties instead of the whole ledger
    // table. TallyPushedAt is set by SaleOrd.SyncAgent once it has actually
    // pushed this party into Tally as a new ledger master — approving a party
    // here only flips ApprovalStatus; the Agent, not the web app, does the
    // actual Tally push (the web app has no direct route to the client's
    // Tally instance — see TallySync:Mode=Agent).
    public bool IsManuallyCreated { get; set; }
    public DateTime? TallyPushedAt { get; set; }

    // Set by an Admin at Party Approval (Admin > Pending Parties), picked
    // from that company's Zone list (see Zone) — denormalized to a plain
    // string here on purpose rather than a ZoneId FK, so the Agent (which
    // reads this Ledger row via a flat Dapper SELECT *, not EF) doesn't need
    // any awareness of the Zones table at all. Pushed to Tally as a UDF
    // field if Settings > Order Defaults > Tally Zone UDF Field is set (see
    // TallyService.PushLedgerAsync), same mechanism as FSSAINo.
    public string? ZoneName { get; set; }

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
