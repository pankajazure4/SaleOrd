namespace SaleOrd.SyncAgent.Models;

// Mirrors the web app's Models/Domain/StockItemTaxSlab.cs — see there for
// design notes.
public class StockItemTaxSlab
{
    public int StockItemTaxSlabId { get; set; }
    public int StockItemId { get; set; }
    public int CompanyId { get; set; }
    public DateTime ApplicableFrom { get; set; }

    public decimal CGSTRate { get; set; }
    public decimal SGSTRate { get; set; }
    public decimal IGSTRate { get; set; }
    public decimal CessRate { get; set; }
    public decimal StateCessRate { get; set; }
}
