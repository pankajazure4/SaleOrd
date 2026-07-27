namespace SaleOrd.Models.Domain;

// Customer+Item last sale rate, pulled from Tally's actual Sales vouchers
// during master sync — this is the client's required rate source, distinct
// from (and takes priority over) SaleOrderItems history, which only reflects
// orders placed inside this app.
public class LastSaleRate
{
    public int LastSaleRateId { get; set; }
    public int CompanyId { get; set; }
    public int LedgerId { get; set; }
    public int StockItemId { get; set; }
    public decimal Rate { get; set; }
    public DateTime SaleDate { get; set; }
    public DateTime LastSyncedAt { get; set; }

    public Company? Company { get; set; }
    public Ledger? Ledger { get; set; }
    public StockItem? StockItem { get; set; }
}

// Raw record from a Tally Sales-voucher export, before PartyName/ItemName
// are resolved to LedgerId/StockItemId against this company's synced masters.
public class SaleRateRecord
{
    public string PartyName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal Rate { get; set; }
    public DateTime SaleDate { get; set; }
}
