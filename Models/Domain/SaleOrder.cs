namespace SaleOrd.Models.Domain;

public class SaleOrder
{
    public int SaleOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; } = DateTime.Today;
    public DateTime? DeliveryDate { get; set; }
    public int LedgerId { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string? Narration { get; set; }
    public decimal TotalAmount { get; set; }   // subtotal (sum of items before tax)

    // Tax fields
    public string TaxType { get; set; } = "None"; // None | IGST | CGST_SGST
    public decimal TaxPercent { get; set; }
    public decimal IGSTTotal { get; set; }
    public decimal CGSTTotal { get; set; }
    public decimal SGSTTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }    // TotalAmount + TaxTotal + RoundOff

    public OrderStatus Status { get; set; } = OrderStatus.Cancelled;
    public string? TallyVoucherNo { get; set; }
    public string? SyncError { get; set; }

    // Set when a Sale Order that had already been pushed to Tally
    // (TallyVoucherNo != null) is cancelled here — tells the Agent it still
    // owes Tally an ACTION="Cancel" push for that voucher. Cleared once that
    // push succeeds. An order cancelled before ever reaching Tally
    // (TallyVoucherNo still null) never sets this — there's nothing in Tally
    // to cancel.
    public bool CancelPushPending { get; set; }
    public DateTime? CancelPushedAt { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SyncedAt { get; set; }
    public DateTime? EditDeadline { get; set; }

    // Invoice tracking from Tally
    public bool IsInvoiced { get; set; }
    public string? TallyInvoiceNo { get; set; }
    public DateTime? TallyInvoiceDate { get; set; }

    public Company? Company { get; set; }
    public Ledger? Ledger { get; set; }
    public AppUser? CreatedBy { get; set; }
    public ICollection<SaleOrderItem> Items { get; set; } = new List<SaleOrderItem>();
}

public enum OrderStatus
{
    // Underlying value 0 is unchanged from the old "Draft" name — every
    // existing row keeps meaning exactly what it already meant (Cancel sets
    // this), just relabeled since "Draft" read as an unsubmitted
    // work-in-progress order, which this never was.
    Cancelled = 0,
    Pending = 1,
    Synced = 2,
    Error = 3
}
