namespace SalesPush.SyncAgent.Models;

// A pending sale, as returned by GET pending-orders — pushed into Tally as
// a Sales Invoice voucher. This is the one canonical JSON contract every
// client's Sales API is expected to send (see
// docs/sales-invoice-contract.json and the non-inventory example next to
// it) — LedgerEntries/AccountingAllocations/Udf are open, so a client's own
// tax setup, extra charges, or UDF fields never need a code change here,
// only a change in what that client's API sends. This agent has no local
// database — the API is the only source of truth it talks to besides Tally
// itself.
public class SaleInvoice
{
    public int SaleInvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; } = DateTime.Today;

    // Some voucher types carry a clock time (Tally's BASICDATETIMEOFINVOICE)
    // that plain InvoiceDate doesn't capture. Not currently written into the
    // pushed XML — kept for when that's confirmed against a real sample.
    public string? InvoiceTime { get; set; }

    public int CompanyId { get; set; }

    // Expected to match Tally's SVCURRENTCOMPANY exactly. Falls back to
    // AgentConfig.TallyCompanyName when a client's API doesn't send one
    // (i.e. a single-company install with no per-order company concept).
    public string CompanyName { get; set; } = string.Empty;

    // Falls back to AgentConfig.VoucherType when the API doesn't send one.
    public string? VoucherType { get; set; }

    // Falls back to AgentConfig.VoucherClass when the API doesn't send one.
    // Confirmed against a real exported voucher: this UAE VAT voucher type
    // ("Sales AY TAX") carries a fixed CLASSNAME ("Default Voucher Class")
    // on every voucher — likely needed for the class's VAT ledger defaults
    // to apply correctly. Left blank/omitted for clients that don't use
    // voucher classes.
    public string? VoucherClass { get; set; }

    public string PartyLedgerName { get; set; } = string.Empty;
    public string? PartyState { get; set; }
    public string? PartyEmirate { get; set; }
    public string? PartyCountry { get; set; }
    public string? PartyTRN { get; set; }

    // Comma-separated address lines — optional, confirmed present (as
    // ADDRESS.LIST/BASICBUYERADDRESS.LIST) on some real vouchers and simply
    // omitted on others, so left out entirely when blank.
    public string? PartyAddress { get; set; }

    // Open voucher-level ledger lines (VAT, discount, freight, round-off...)
    // — see LedgerEntry.cs. The party's own balancing entry is computed
    // mechanically from these plus the items, never sent by the API.
    public List<LedgerEntry> LedgerEntries { get; set; } = new();

    // Raw Tally UDF field name -> value, e.g. {"eiAreaName": "UNION MALL"}.
    // Keys are pushed verbatim as <UDF:{key}> tags, so they must already be
    // Tally's own internal UDF field names.
    public Dictionary<string, string> Udf { get; set; } = new();

    public string? EnteredBy { get; set; }

    public List<SaleInvoiceItem> Items { get; set; } = new();
}
