namespace SalesPush.SyncAgent.Models;

// A pending sale, as returned by the Sales API — pushed into Tally as a
// Sales Invoice voucher. Party details (GSTNo/State/Address/CreditPeriod)
// are embedded directly on the invoice rather than resolved from a local
// ledger master, since this agent has no local database — the API is the
// only source of truth it talks to besides Tally itself.
public class SaleInvoice
{
    public int SaleInvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public int CompanyId { get; set; }
    public string? Narration { get; set; }

    public string PartyLedgerName { get; set; } = string.Empty;
    public string? PartyGSTNo { get; set; }
    public string? PartyState { get; set; }
    public string? PartyAddress { get; set; }
    public string? PartyCreditPeriod { get; set; }

    public decimal TotalAmount { get; set; }

    public string TaxType { get; set; } = "None"; // None | IGST | CGST_SGST
    public decimal IGSTTotal { get; set; }
    public decimal CGSTTotal { get; set; }
    public decimal SGSTTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    public SaleInvoiceStatus Status { get; set; } = SaleInvoiceStatus.Pending;
    public string? TallyVoucherNo { get; set; }
    public string? SyncError { get; set; }
    public DateTime? SyncedAt { get; set; }

    public List<SaleInvoiceItem> Items { get; set; } = new();
}

public enum SaleInvoiceStatus
{
    Pending = 0,
    Synced = 1,
    Error = 2
}
