namespace SaleOrd.SyncAgent.Models;

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
    public decimal TotalAmount { get; set; }

    public string TaxType { get; set; } = "None";
    public decimal TaxPercent { get; set; }
    public decimal IGSTTotal { get; set; }
    public decimal CGSTTotal { get; set; }
    public decimal SGSTTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Cancelled;
    public string? TallyVoucherNo { get; set; }
    public string? SyncError { get; set; }
    public DateTime? SyncedAt { get; set; }

    public bool IsInvoiced { get; set; }
    public string? TallyInvoiceNo { get; set; }
    public DateTime? TallyInvoiceDate { get; set; }

    public Company? Company { get; set; }
    public Ledger? Ledger { get; set; }
    public List<SaleOrderItem> Items { get; set; } = new();
}

public enum OrderStatus
{
    // See the web app's OrderStatus for why this was renamed from "Draft".
    Cancelled = 0,
    Pending = 1,
    Synced = 2,
    Error = 3
}
