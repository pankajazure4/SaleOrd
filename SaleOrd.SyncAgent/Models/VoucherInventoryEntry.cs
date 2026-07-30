namespace SaleOrd.SyncAgent.Models;

// Mirrors the web app's Models/Domain/VoucherInventoryEntry.cs — one row per
// (Sales-type voucher, stock item line). See VoucherInventorySyncService for
// the full/incremental sync design.
public class VoucherInventoryEntry
{
    public int VoucherInventoryEntryId { get; set; }
    public int CompanyId { get; set; }

    public string VoucherGUID { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public DateTime VoucherDate { get; set; }
    public long AlterId { get; set; }

    public string PartyLedgerName { get; set; } = string.Empty;
    public int? LedgerId { get; set; }

    public string StockItemName { get; set; } = string.Empty;
    public int? StockItemId { get; set; }

    public decimal ActualQty { get; set; }
    public decimal BilledQty { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public string? GodownName { get; set; }

    public DateTime LastSyncedAt { get; set; }
}
