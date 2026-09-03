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

    // Display-only — the server always recomputes these from the item's own
    // StockItemTaxSlabs on save, never trusts what's posted here. Carried on
    // the VM purely so the Edit screen's live total preview shows an
    // existing order's saved rates immediately, without an extra round trip
    // per line to re-fetch them.
    public decimal CGSTRate { get; set; }
    public decimal SGSTRate { get; set; }
    public decimal IGSTRate { get; set; }
}

public class SaleOrderListVM
{
    public int SaleOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsInvoiced { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? SyncError { get; set; }

    // What the status badge actually shows — a Synced order that's since
    // been invoiced in Tally reads as "Invoiced", not "Synced", everywhere
    // this label is used (order list, details, dashboard, reports).
    public string StatusLabel => Status == OrderStatus.Synced && IsInvoiced ? "Invoiced" : Status.ToString();
}
