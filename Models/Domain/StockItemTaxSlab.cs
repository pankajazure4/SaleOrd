namespace SaleOrd.Models.Domain;

// One dated GST rate slab for a stock item, mirroring Tally's own
// GSTDETAILS.LIST — an item's GST% can change over time (rate notifications),
// so each sync replaces the full set of slabs for an item (delete + reinsert)
// rather than keeping a single current rate. Order tax calc picks the latest
// slab whose ApplicableFrom <= the order date.
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

    public StockItem? StockItem { get; set; }
}
