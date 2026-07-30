namespace SaleOrd.Models.Domain;

public class Ledger
{
    public int LedgerId { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;

    // Contact
    public string? Address { get; set; }
    public string? State { get; set; }
    public string? MobileNo { get; set; }
    public string? Email { get; set; }
    public string? LedgerFax { get; set; }

    // GST / Tax
    public string? GSTNo { get; set; }           // PartyGSTIN
    public string? TaxType { get; set; }
    public string? IncomeTaxNo { get; set; }
    public string? VATTINNo { get; set; }
    public string? FSSAINo { get; set; }          // Food License No. — local only, no Tally equivalent

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
}
