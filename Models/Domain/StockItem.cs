namespace SaleOrd.Models.Domain;

public class StockItem
{
    public int StockItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;     // Stock Group
    public string UOM { get; set; } = string.Empty;        // BaseUnits
    public string? AdditionalUnits { get; set; }            // Compound unit
    public decimal Rate { get; set; }                       // StandardPrice (from voucher rate)
    public decimal RateOfDuty { get; set; }
    public string? HSNCode { get; set; }
    public string? Description { get; set; }

    // Stock quantities
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal OpeningValue { get; set; }
    public decimal ClosingValue { get; set; }

    // Flags
    public bool IsBatchwiseOn { get; set; }
    public bool IsCostTrackingOn { get; set; }

    // Sync tracking
    public string? GUID { get; set; }
    public long AlterId { get; set; }

    public int CompanyId { get; set; }
    public DateTime LastSyncedAt { get; set; }

    public Company? Company { get; set; }
}
