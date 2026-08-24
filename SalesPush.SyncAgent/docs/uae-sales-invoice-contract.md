# UAE Sales Invoice API Contract — field notes

**Revision 3** — added `companyName` alongside `companyId` (belt-and-braces,
see "Company name" section below); fixed `saleInvoiceId` to no longer
duplicate `invoiceNo`'s value; added a non-inventory (no stock items) example.

**Revision 2** — reworked per feedback: `ledgerEntries` and each item's
`accountingAllocations` are now open arrays of `{ledgerName, amount}`, not
named fields like `taxLedgerName`/`taxAmount`. The API just throws whatever
ledger lines apply (VAT, discount, freight, rounding, whatever) with a name
and an already-computed amount — nothing here derives a rate or calculates
tax. The party ledger's balancing entry isn't in the JSON at all; the agent
computes it mechanically as `-(sum of item amounts + sum of ledgerEntries)`
when building the XML, the same arithmetic Tally itself requires for a
voucher to balance — that's bookkeeping, not the kind of rate/% calculation
that caused the earlier trouble, and it's the same way `SaleInvoice.GrandTotal`
already gets verified today.


Derived from a real exported voucher (`Sales_19651.xml`, VCHTYPE "Sales AY TAX",
party AL SHIRAA ALLAMEI BUILDING MATERIAL TRD CO LLC). Everything Tally emits
that ISN'T here (the long list of `<ISxxx>No</ISxxx>` flags, empty `.LIST`
blocks, GUID/ALTERID/MASTERID/VOUCHERKEY) is either a Tally-side default that
gets applied automatically on `ACTION="Create"`, or a value Tally *returns*
after creation — none of it is something the source API needs to send.

## Header — what's actually from the XML vs. what I filled in

Being explicit here since it matters: some fields below are real values pulled
straight from the source XML, and some are placeholder values I made up to
show the field's *shape* — these must not be confused for one another.

| JSON field | Real, from XML? | Source / notes |
|---|---|---|
| `saleInvoiceId` | **No — fabricated example (90042)** | This has to be the client's own internal DB primary key for the sale record. It doesn't exist anywhere in Tally's data — Tally has no concept of it. Earlier draft mistakenly reused `19651` (the voucher number) for this too, which wrongly implied it came from the XML — fixed now to a different example value so the two are visibly distinct fields. |
| `invoiceNo` | **Yes** | `VOUCHERNUMBER` = "19651". Forced into Tally as the actual voucher number (not auto-numbered) — matches how the existing agent already handles Create vs Alter by voucher number. Confirm this assumption with the client if unsure. |
| `invoiceDate` | **Yes** | `DATE` = 20250402, reformatted to ISO. |
| `invoiceTime` | **Yes** | `BASICDATETIMEOFINVOICE` = "2-Apr-2025 at **09:30**" — the time portion, extracted. |
| `companyId` | **No — fabricated example (12)** | See "Company name" section below — this is a real design question, not just a placeholder. |
| `voucherType` | **Yes** | `VOUCHERTYPENAME` / `VCHTYPE` attribute = "Sales AY TAX". Different literal value than the existing `VoucherType` config default ("Sales") — confirm whether this is fixed per company/config, or genuinely varies per invoice. |

## Company name — it IS in the XML, just not per-voucher

You're right that no company name shows up in the JSON — but it does exist in
the source XML, at the very top, **once per request** (not per voucher):

```xml
<STATICVARIABLES>
  <SVCURRENTCOMPANY>ASGAR ALI YOUSIF TRADING CO LLC - MB (2022-26) - (from 1-Jan-25)</SVCURRENTCOMPANY>
</STATICVARIABLES>
```

This is exactly how the *existing* agent already works today: `SVCURRENTCOMPANY`
isn't sent per-invoice by the API at all — the agent sets it once when it
builds the whole Import Data envelope, by resolving `companyId` against its
own `Company` list (`ApiDataService.GetActiveCompaniesAsync`) to get that
company's `TallyCompanyName`. One API request can contain vouchers for one
company at a time; the company name lives in the envelope wrapper, not inside
each `SaleInvoice` object. That's why `companyId` (an integer, matching the
existing model) rather than a company name string is in the JSON.

**Decision: send both.** `companyName` now sits alongside `companyId` in the
contract — the exact `SVCURRENTCOMPANY` string, straight from the client's
Tally. `companyId` stays as the primary key the agent matches against its own
`Company` list (same as today's model), and `companyName` is there as a
belt-and-braces fallback/cross-check — if the ID lookup ever comes back empty
or mismatched, the agent still has the real Tally company name to fall back
on or to flag a mismatch against, rather than silently pushing to the wrong
company or failing with no diagnostic. Doesn't require the client to build
anything extra beyond what a `/api/companies`-style endpoint would already
need to return.

## Party

| JSON field | Source in XML | Notes |
|---|---|---|
| `partyLedgerName` | `PARTYLEDGERNAME` | Same role as existing `PartyLedgerName`. |
| `partyState` | `STATENAME` ("Dubai") | Same role as existing `PartyState`. |
| `partyEmirate` | `EMIRATEPOS` | **New** — UAE's VAT "place of supply" concept is the Emirate, not an Indian state. Here it's the same value as `partyState`, but they're semantically distinct fields in Tally and may diverge for another party. |
| `partyCountry` | `COUNTRYOFRESIDENCE` / `PLACEOFSUPPLYCOUNTRY` ("UAE") | Both fields carry "UAE" here — likely constant per company/config rather than per-invoice. Worth confirming it's never anything else for this client. |
| `partyTRN` | `BASICBUYERSSALESTAXNO` | UAE VAT registration number — equivalent role to India's GSTIN. |

## Amounts

**Important structural difference from the existing GST model:** this client uses a **single flat VAT ledger** (`Vat@5%`), not the IGST/CGST/SGST split the current `SaleInvoice.TaxType` (None/IGST/CGST_SGST) assumes. The tax side of this contract needs its own shape for VAT clients rather than reusing `IGSTTotal`/`CGSTTotal`/`SGSTTotal`.

| JSON field | Source in XML | Notes |
|---|---|---|
| `salesLedgerName` | Each item's `ACCOUNTINGALLOCATIONS.LIST > LEDGERNAME` ("SHIRA AL LAMEA SALES") | **Structural change from today:** the existing agent has ONE global `SalesLedger` from config, used for every push. In this voucher, every line item points at the *same* sales ledger, but that ledger's name is specific to this party/branch ("SHIRA AL LAMEA SALES" — not a generic "Sales"). Need to confirm with the client whether this is fixed per company (config-level, like today) or genuinely varies — if it can vary by party, it needs to travel per-invoice in the JSON (as modeled here) rather than living in agent config. |
| `taxLedgerName` | `LEDGERENTRIES.LIST > LEDGERNAME` ("Vat@5%") | The VAT ledger used for the tax line. Likely varies by VAT rate (e.g. a "Vat@0%" ledger for zero-rated sales) — confirm whether the client's API will send different ledger names for different rates, or if this stays fixed. |
| `taxAmount` | Same ledger entry's `AMOUNT`/`VATEXPAMOUNT` (404.940) | |
| `itemsTotal` | Sum of all item `AMOUNT`s (8098.800) | Verified: sum of the 12 item amounts below equals exactly this. |
| `grandTotal` | Party ledger's `AMOUNT`, sign-flipped (`-8503.740` → `8503.740`) | Verified: `itemsTotal + taxAmount = grandTotal` exactly (8098.800 + 404.940 = 8503.740). The party ledger carries the *negative* of this (credit side) — the agent computes/verifies this the same way the existing GrandTotal logic already does. |

## Custom fields (UDFs)

These come through as `<UDF:_UDF_xxxxx>` / named UDF tags in the `xmlns:UDF="TallyUDF"` namespace — client-specific TDL customizations, not standard Tally fields. **Three of the five have no human-readable internal name** (`_UDF_788534172`, `_UDF_788537002`, `_UDF_788537005` — just auto-generated IDs), so my field names below (`deliveryMode`, `paymentMode`, `paymentStatus`) are **my best guess from context, not confirmed** — please verify the actual business meaning with whoever configured these UDFs in the client's Tally before this goes into the real contract:

| JSON field (my guess) | Tally UDF | Value seen | Confidence |
|---|---|---|---|
| `deliveryMode` | `_UDF_788534172` (index 5019) | "Cash On Delivery" | Guess — could also be "Order Type" or similar |
| `paymentMode` | `_UDF_788537002` (index 7849) | "Cash" | Guess |
| `paymentStatus` | `_UDF_788537005` (index 7852) | "Cash" | Guess — same value as paymentMode in this example, so hard to tell them apart from one sample alone |
| `agentCode` | `VCHAgentNameStoUDf` | "SC" | Confirmed — named field, clearly a sales agent code |
| `areaName` | `EiAreaName` | "UNION MALL" | Confirmed — named field |
| `cityCode` | `UDFCitySto` | "SHJ" (Sharjah) | Confirmed — named field |

`enteredBy` (from `ENTEREDBY`, "Neha") isn't a UDF — it's a standard field, included here as optional metadata (who keyed the original invoice), probably not essential to push back into Tally but useful for traceability/logs.

## Items (array)

Straightforward, one entry per `ALLINVENTORYENTRIES.LIST`:

| JSON field | Source | Notes |
|---|---|---|
| `stockItemName` | `STOCKITEMNAME` | |
| `rate` | `RATE` ("5.600/Pcs") | Split into `rate` (numeric) + `unit` — the XML carries them combined as text. |
| `unit` | `RATE` suffix | Always "Pcs" in this sample; may vary by item. |
| `actualQty` / `billedQty` | `ACTUALQTY` / `BILLEDQTY` | Identical in every line here (no free/damaged qty in this invoice) — kept as two separate fields since Tally itself distinguishes them (would matter for e.g. returns or free-quantity schemes). |
| `amount` | `AMOUNT` | |
| `godownName` | `BATCHALLOCATIONS.LIST > GODOWNNAME` ("AJM") | Same value repeated for `DESTINATIONGODOWNNAME` in the XML — a straight transfer within one godown, not a multi-godown movement, so only one field needed. |
| `batchName` | `BATCHALLOCATIONS.LIST > BATCHNAME` | "Primary Batch" for every line here — same as the existing agent's config default. |

## What's deliberately left out

- All `IS*` boolean flags (`ISDEEMEDPOSITIVE`, `ISCANCELLED`, `ISOPTIONAL`, etc.) — Tally defaults these correctly on `ACTION="Create"`.
- `GUID`, `ALTERID`, `MASTERID`, `VOUCHERKEY`, `VOUCHERRETAINKEY` — these are Tally-assigned identifiers, returned *after* creation (used for a later Alter), not something we send in.
- The trailing `<TALLYMESSAGE><COMPANY>...` blocks (`REMOTECMPINFO.LIST`) — Tally's own remote-company GUID↔name bookkeeping, emitted automatically on export, irrelevant to a push.
- `NUMBERINGSTYLE`, `GSTREGISTRATION` ("Not Applicable" — this is a VAT company, GST fields are just inert here) — config/company-level, not per-invoice data.

## Sales with no inventory (service invoices etc.)

`items` is just an array — for a sale with no stock items at all, it's empty
(`"items": []`), and whatever was actually sold becomes another line in the
voucher-level `ledgerEntries` array (an income/service ledger) sitting
alongside the tax line, instead of coming from any item's
`accountingAllocations`. The party's balancing entry is computed exactly the
same mechanical way either way — `-(sum of item amounts + sum of
ledgerEntries)` — it doesn't care whether the amount came from items or from
ledgerEntries. Full working example: `uae-sales-invoice-contract-noninventory-example.json`.

```json
{
  "invoiceNo": "19652",
  "ledgerEntries": [
    { "ledgerName": "Consultancy Income", "amount": 5000.000 },
    { "ledgerName": "Vat@5%", "amount": 250.000 }
  ],
  "items": []
}
```

## Open questions before this is final

1. Confirm the real meaning of the three unnamed UDFs (`deliveryMode`/`paymentMode`/`paymentStatus` guesses above).
2. Confirm whether `salesLedgerName` and `taxLedgerName` are genuinely per-invoice/per-party, or fixed per company (in which case they belong in agent config like the existing `SalesLedger`/`IGSTLedger` fields, not in the per-invoice JSON).
3. Confirm whether `invoiceNo` should force the Tally voucher number (current assumption) or if Tally should auto-number and we just track the mapping.
