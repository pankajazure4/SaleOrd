namespace SaleOrd.SyncAgent.Models;

public class StockItem
{
    public int StockItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string UOM { get; set; } = string.Empty;
    public string? AdditionalUnits { get; set; }
    public decimal Rate { get; set; }
    public decimal RateOfDuty { get; set; }
    public string? HSNCode { get; set; }
    public string? Description { get; set; }

    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal OpeningValue { get; set; }
    public decimal ClosingValue { get; set; }

    public bool IsBatchwiseOn { get; set; }
    public bool IsCostTrackingOn { get; set; }

    public string? GUID { get; set; }
    public long AlterId { get; set; }

    public int CompanyId { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
