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

    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public string? TallyVoucherNo { get; set; }
    public string? SyncError { get; set; }
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
    Draft = 0,
    Pending = 1,
    Synced = 2,
    Error = 3
}
