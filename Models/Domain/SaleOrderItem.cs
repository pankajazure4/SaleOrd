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

    // Tax snapshot at the time this line was saved — resolved from the
    // item's own StockItemTaxSlabs as of the order date, not a flat order-
    // wide %. Rate = the % applied; Amount = Amount * Rate / 100.
    public decimal CGSTRate { get; set; }
    public decimal SGSTRate { get; set; }
    public decimal IGSTRate { get; set; }
    public decimal CGSTAmount { get; set; }
    public decimal SGSTAmount { get; set; }
    public decimal IGSTAmount { get; set; }

    public SaleOrder? SaleOrder { get; set; }
    public StockItem? StockItem { get; set; }
    public Godown? Godown { get; set; }
}
