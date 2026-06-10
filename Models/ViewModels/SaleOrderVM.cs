using System.ComponentModel.DataAnnotations;
using SaleOrd.Models.Domain;

namespace SaleOrd.Models.ViewModels;

public class SaleOrderCreateVM
{
    public int? SaleOrderId { get; set; }

    [Required]
    public int LedgerId { get; set; }
    public string LedgerName { get; set; } = string.Empty;

    [Required]
    public DateTime OrderDate { get; set; } = DateTime.Today;
    public DateTime? DeliveryDate { get; set; }
    public string? Narration { get; set; }

    // Tax fields
    public string TaxType { get; set; } = "None";     // None | IGST | CGST_SGST
    public decimal TaxPercent { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    public List<SaleOrderItemVM> Items { get; set; } = new();
}

public class SaleOrderItemVM
{
    public int StockItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string UOM { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Discount { get; set; }
    public decimal Amount { get; set; }
    public int? GodownId { get; set; }
    public string? GodownName { get; set; }
}

public class SaleOrderListVM
{
    public int SaleOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? SyncError { get; set; }
}
