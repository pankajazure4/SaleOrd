namespace SalesPush.SyncAgent.Models;

// One open {ledgerName, amount} line — no calculation happens on our side,
// the API sends the already-computed amount and we push it through
// mechanically. Used two places in SaleInvoice:
//   - SaleInvoice.LedgerEntries (voucher-level): VAT, discount, freight,
//     round-off... whatever the client's API decides applies to this sale.
//   - SaleInvoiceItem.AccountingAllocations (item-level): which sales
//     ledger(s) an item's amount is booked against — lets sales ledger be
//     party/branch-specific instead of one fixed agent-config value.
// This open-array shape (rather than named fields like TaxLedgerName/
// TaxAmount) is the actual mechanism that keeps this agent generic across
// clients — see docs/sales-invoice-contract.json.
public class LedgerEntry
{
    public string LedgerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
