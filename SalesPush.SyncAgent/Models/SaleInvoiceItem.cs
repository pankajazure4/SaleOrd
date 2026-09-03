namespace SalesPush.SyncAgent.Models;

public class SaleInvoiceItem
{
    public string StockItemName { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal ActualQty { get; set; }
    public decimal BilledQty { get; set; }
    public decimal Amount { get; set; }
    public string? GodownName { get; set; }

    // Falls back to AgentConfig.BatchName when the API doesn't send one.
    public string? BatchName { get; set; }

    // Which sales ledger(s) this item's Amount is booked against — see
    // LedgerEntry.cs. Almost always one entry, but left as a list since
    // nothing stops a client from splitting one item across ledgers.
    public List<LedgerEntry> AccountingAllocations { get; set; } = new();
}
