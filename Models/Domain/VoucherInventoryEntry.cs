namespace SaleOrd.Models.Domain;

// One row per (Sales-type voucher, stock item line) pulled from Tally —
// replaces the old derived-only "last sale rate" summary with the actual
// line-level data, synced via a one-time full historical batch pass
// (VoucherDate range, chunked) followed by ongoing incremental syncs keyed
// on Tally's AlterId watermark (Company.LastVoucherAlterId). Re-syncing a
// voucher (e.g. it was altered in Tally) replaces all of that voucher's
// rows rather than appending duplicates — see VoucherGUID.
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

    public Company? Company { get; set; }
    public Ledger? Ledger { get; set; }
    public StockItem? StockItem { get; set; }
}
