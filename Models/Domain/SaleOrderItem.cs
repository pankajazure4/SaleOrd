namespace SaleOrd.Models.Domain;

public class SaleOrderItem
{
    public int SaleOrderItemId { get; set; }
    public int SaleOrderId { get; set; }
    public int StockItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string UOM { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Discount { get; set; }
    public decimal Amount { get; set; }
    public int? GodownId { get; set; }
    public string? GodownName { get; set; }

    public SaleOrder? SaleOrder { get; set; }
    public StockItem? StockItem { get; set; }
    public Godown? Godown { get; set; }
}
