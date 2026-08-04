namespace SalesPush.SyncAgent.Models;

public class SaleInvoiceItem
{
    public int SaleInvoiceItemId { get; set; }
    public int SaleInvoiceId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string UOM { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Discount { get; set; }
    public decimal Amount { get; set; }
    public string? GodownName { get; set; }
}
