namespace SaleOrd.SyncAgent.Models;

public class LastSaleRate
{
    public int LastSaleRateId { get; set; }
    public int CompanyId { get; set; }
    public int LedgerId { get; set; }
    public int StockItemId { get; set; }
    public decimal Rate { get; set; }
    public DateTime SaleDate { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

// Raw (party name, item name, rate) tuple pulled from Tally before it's
// resolved to local LedgerId/StockItemId — mirrors the web app's shape.
public class SaleRateRecord
{
    public string PartyName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal Rate { get; set; }
    public DateTime SaleDate { get; set; }
}
