using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SalesPush.SyncAgent.Models;

namespace SalesPush.SyncAgent.Services;

// Talks to Tally's local XML HTTP gateway. Ported from SaleOrd.SyncAgent's
// TallyService — same XML plumbing (invalid-char stripping, UDF namespace
// fix, parsing helpers) — but trimmed down to just what a Sales Invoice
// push needs: no master sync, no voucher-inventory history, no invoice
// look-up/matching, since this agent pushes a finished invoice directly
// rather than a Sales Order that later gets converted in Tally.
public class TallyService
{
    private readonly HttpClient _http;
    private readonly AgentLogger _logger;

    public TallyService(HttpClient http, AgentLogger logger)
    {
        _http = http;
        _logger = logger;
    }

    private async Task<XDocument?> PostXmlAsync(string url, string xml)
    {
        try
        {
            var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            var response = await _http.PostAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            return XDocument.Parse(DeclareUdfNamespace(StripInvalidXmlChars(body)));
        }
        catch (Exception ex)
        {
            _logger.Error($"Tally XML request failed for {url}", ex);
            return null;
        }
    }

    public async Task<(bool Reachable, List<string> OpenCompanies)> CheckStatusAsync(string tallyUrl)
    {
        try
        {
            var xml = "<ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>List of Companies</ID></HEADER><BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES></DESC></BODY></ENVELOPE>";
            var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _http.PostAsync(tallyUrl, content, cts.Token);
            if (!response.IsSuccessStatusCode) return (false, new());

            var body = await response.Content.ReadAsStringAsync();
            var doc  = XDocument.Parse(body);
            var companies = doc.Descendants("COMPANY")
                .Select(c => c.Attribute("NAME")?.Value ?? c.Element("NAME")?.Value ?? "")
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            return (true, companies);
        }
        catch { return (false, new()); }
    }

    // Guards against re-pushing a sale that's already in Tally — e.g. a
    // prior cycle's push actually succeeded but the API's "mark as synced"
    // callback failed (network blip, API-side error), so pending-orders
    // keeps handing back the same invoice forever. Pushing it again risks
    // a duplicate voucher or a Tally exception on the reused REMOTEID.
    // Matched on VoucherTypeName + VoucherNumber — now that
    // PushSalesInvoiceAsync sets VOUCHERNUMBER = invoice.InvoiceNo
    // explicitly (2026-09-03, per client request, rather than leaving it
    // to Tally's auto-numbering), this can filter on VoucherNumber same as
    // LoheBgService's proven-working exists-check, instead of the earlier
    // Reference-based filter (2026-09-03 test: matched a "voucher" with
    // every field blank — a $Reference filter that likely wasn't actually
    // being applied by Tally; switched to VoucherNumber to fix that).
    public async Task<bool> VoucherExistsAsync(string tallyUrl, string companyName, string voucherTypeName, string voucherNumber)
    {
        if (string.IsNullOrWhiteSpace(voucherNumber)) return false;

        var fetchXml = $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>EXPORT</TALLYREQUEST>
    <TYPE>COLLECTION</TYPE>
    <ID>SalesPushVoucherExistsCheck</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
      </STATICVARIABLES>
      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME=""SalesPushVoucherExistsCheck"" ISINITIALIZE=""Yes"">
            <TYPE>Voucher</TYPE>
            <FETCH>Date, VoucherNumber, VoucherTypeName, Reference, MasterId</FETCH>
            <FILTERS>TypeFilter, NumberFilter</FILTERS>
          </COLLECTION>
          <SYSTEM TYPE=""Formulae"" NAME=""TypeFilter"">
            $VoucherTypeName = ""{Escape(voucherTypeName)}""
          </SYSTEM>
          <SYSTEM TYPE=""Formulae"" NAME=""NumberFilter"">
            $VoucherNumber = ""{Escape(voucherNumber)}""
          </SYSTEM>
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var content = new StringContent(fetchXml, Encoding.UTF8, "application/xml");
            var response = await _http.PostAsync(tallyUrl, content, cts.Token);
            // Inconclusive on any failure — never block a genuine push on a
            // guess; only skip when Tally positively confirms the voucher
            // is already there.
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn($"VoucherExistsAsync: HTTP {(int)response.StatusCode} for VoucherNumber='{voucherNumber}' — treating as not-found.");
                return false;
            }

            var raw = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(DeclareUdfNamespace(StripInvalidXmlChars(raw)));
            var vouchers = doc.Descendants("VOUCHER").ToList();

            // Diagnostic while this guard is still being validated
            // (2026-09-03, switched from a Reference filter that matched a
            // blank-fields "voucher" — likely never actually applied by
            // Tally) — dumps every matched voucher's own VoucherNumber/
            // Reference so a bad match is obvious from the log.
            _logger.Info($"VoucherExistsAsync: Type='{voucherTypeName}' VoucherNumber='{voucherNumber}' -> {vouchers.Count} voucher(s) matched.");
            foreach (var v in vouchers.Take(10))
            {
                var vNo = v.Element("VOUCHERNUMBER")?.Value ?? "";
                var vRef = v.Element("REFERENCE")?.Value ?? "";
                var vDate = v.Element("DATE")?.Value ?? "";
                _logger.Info($"    matched voucher: Number='{vNo}' Reference='{vRef}' Date='{vDate}'");
            }

            return vouchers.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.Warn($"VoucherExistsAsync failed for VoucherNumber='{voucherNumber}': {ex.Message} — treating as not-found.");
            return false;
        }
    }

    // Pushes one SaleInvoice into Tally as a Sales Invoice voucher (ISINVOICE
    // = Yes, unlike SaleOrd.SyncAgent's Sales Order push). Always
    // ACTION="Create" — this agent has no local record of what it already
    // pushed (no local DB), and the contract has no notion of "this is a
    // correction, Alter the existing voucher" yet. If a push fails partway
    // (e.g. network drops after Tally already created the voucher but
    // before we read the response), the next cycle will try to Create the
    // same REMOTEID again — Tally's own duplicate-REMOTEID handling is what
    // guards against that today; revisit if that turns out not to be enough
    // once real usage/the voucher XML sample is in.
    //
    // Ledger/tax lines and item sales-ledger splits are entirely generic —
    // built from invoice.LedgerEntries and item.AccountingAllocations
    // rather than named fields, so nothing here is India/GST- or UAE/VAT-
    // specific. The party/registration block was reconciled against two
    // real exported "Sales AY TAX" (UAE VAT) vouchers — see field-by-field
    // notes inline below.
    public async Task<(bool Success, string Message)> PushSalesInvoiceAsync(
        string tallyUrl, SaleInvoice invoice, string companyName,
        string defaultVoucherType  = "Sales",
        string defaultBatchName   = "Primary Batch",
        string defaultVoucherClass = "",
        bool   isOptional         = true)
    {
        var dateStr     = invoice.InvoiceDate.ToString("yyyyMMdd");
        var voucherType = string.IsNullOrWhiteSpace(invoice.VoucherType) ? defaultVoucherType : invoice.VoucherType;
        var narration   = string.IsNullOrWhiteSpace(invoice.EnteredBy)
            ? $"Ref: {invoice.InvoiceNo}"
            : $"Ref: {invoice.InvoiceNo} | Entered by {invoice.EnteredBy}";
        var remoteId    = $"salespush{invoice.SaleInvoiceId:D7}";

        var itemsXml = new StringBuilder();
        foreach (var item in invoice.Items)
        {
            // 3 decimal places, not 2 — confirmed against real exports:
            // every RATE/AMOUNT in both sample vouchers (and the original
            // contract JSON) is 3-decimal ("6.400/Kg", "640.000"), not 2.
            var rateStr    = $"{item.Rate:F3}/{item.Unit}";
            var actualQty  = $" {item.ActualQty:F3} {item.Unit}";
            var billedQty  = $" {item.BilledQty:F3} {item.Unit}";
            var batchName  = string.IsNullOrWhiteSpace(item.BatchName) ? defaultBatchName : item.BatchName;

            var batchXml = string.Empty;
            if (!string.IsNullOrWhiteSpace(item.GodownName))
            {
                batchXml = $@"
              <BATCHALLOCATIONS.LIST>
                <GODOWNNAME>{Escape(item.GodownName)}</GODOWNNAME>
                <BATCHNAME>{Escape(batchName)}</BATCHNAME>
                <DESTINATIONGODOWNNAME>{Escape(item.GodownName)}</DESTINATIONGODOWNNAME>
                <AMOUNT>{item.Amount:F3}</AMOUNT>
                <ACTUALQTY>{actualQty}</ACTUALQTY>
                <BILLEDQTY>{billedQty}</BILLEDQTY>
                <ADDITIONALDETAILS.LIST></ADDITIONALDETAILS.LIST>
                <VOUCHERCOMPONENTLIST.LIST></VOUCHERCOMPONENTLIST.LIST>
              </BATCHALLOCATIONS.LIST>";
            }

            var allocationsXml = new StringBuilder();
            foreach (var alloc in item.AccountingAllocations)
            {
                allocationsXml.Append($@"
              <ACCOUNTINGALLOCATIONS.LIST>
                <LEDGERNAME>{Escape(alloc.LedgerName)}</LEDGERNAME>
                <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
                <LEDGERFROMITEM>No</LEDGERFROMITEM>
                <REMOVEZEROENTRIES>No</REMOVEZEROENTRIES>
                <ISPARTYLEDGER>No</ISPARTYLEDGER>
                <GSTOVERRIDDEN>No</GSTOVERRIDDEN>
                <AMOUNT>{alloc.Amount:F3}</AMOUNT>
                <SERVICETATXDETAILS.LIST></SERVICETATXDETAILS.LIST>
                <CATEGORYALLOCATIONS.LIST></CATEGORYALLOCATIONS.LIST>
                <BANKALLOCATION.LIST></BANKALLOCATION.LIST>
                <BILLALLOCATIONS.LIST></BILLALLOCATIONS.LIST>
                <INTERESTCOLLECTION.LIST></INTERESTCOLLECTION.LIST>
                <OLDAUDITENTRIES.LIST></OLDAUDITENTRIES.LIST>
                <ACCOUNTAUDITENTRIES.LIST></ACCOUNTAUDITENTRIES.LIST>
                <AUDITENTRIES.LIST></AUDITENTRIES.LIST>
                <INPUTCRALOCS.LIST></INPUTCRALOCS.LIST>
                <DUTYHEADDETAILS.LIST></DUTYHEADDETAILS.LIST>
                <RATEDETAILS.LIST></RATEDETAILS.LIST>
                <REFVOUCHERDETAILS.LIST></REFVOUCHERDETAILS.LIST>
                <INVOICEWISEDETAILS.LIST></INVOICEWISEDETAILS.LIST>
                <TAXTYPEALLOCATIONS.LIST></TAXTYPEALLOCATIONS.LIST>
              </ACCOUNTINGALLOCATIONS.LIST>");
            }

            itemsXml.Append($@"
            <ALLINVENTORYENTRIES.LIST>
              <STOCKITEMNAME>{Escape(item.StockItemName)}</STOCKITEMNAME>
              <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
              <RATE>{rateStr}</RATE>
              <AMOUNT>{item.Amount:F3}</AMOUNT>
              <ACTUALQTY>{actualQty}</ACTUALQTY>
              <BILLEDQTY>{billedQty}</BILLEDQTY>
              {batchXml}
              {allocationsXml}
              <DUTYHEADDETAILS.LIST></DUTYHEADDETAILS.LIST>
              <RATEDETAILS.LIST></RATEDETAILS.LIST>
              <SUPPLEMENTARYDUTYHEADDETAILS.LIST></SUPPLEMENTARYDUTYHEADDETAILS.LIST>
              <TAXOBJECTALLOCATIONS.LIST></TAXOBJECTALLOCATIONS.LIST>
              <REFVOUCHERDETAILS.LIST></REFVOUCHERDETAILS.LIST>
              <EXCISEALLOCATIONS.LIST></EXCISEALLOCATIONS.LIST>
              <EXPENSEALLOCATIONS.LIST></EXPENSEALLOCATIONS.LIST>
            </ALLINVENTORYENTRIES.LIST>");
        }

        // Party's own balancing entry — the only amount we compute
        // ourselves, plain addition to satisfy Tally's debit=credit
        // requirement (never a tax/rate calculation): -(sum of item
        // amounts + sum of voucher-level ledgerEntries amounts).
        var partyAmount = invoice.Items.Sum(i => i.Amount) + invoice.LedgerEntries.Sum(l => l.Amount);

        // Bill-wise reference on the party's own entry — confirmed present
        // on every real voucher sampled, registered and cash-sale party
        // alike, so treated as always-required rather than optional.
        // NAME uses invoice.InvoiceNo, same as VOUCHERNUMBER below (2026-09-03:
        // explicitly set VOUCHERNUMBER = InvoiceNo per client request, instead
        // of leaving it to Tally's own auto-numbering) — so this now matches
        // the voucher's real number, not a separate reference.
        var billAllocXml = $@"
              <BILLALLOCATIONS.LIST>
                <NAME>{Escape(invoice.InvoiceNo)}</NAME>
                <BILLTYPE>New Ref</BILLTYPE>
                <TDSDEDUCTEEISSPECIALRATE>No</TDSDEDUCTEEISSPECIALRATE>
                <AMOUNT>-{partyAmount:F3}</AMOUNT>
                <INTERESTCOLLECTION.LIST></INTERESTCOLLECTION.LIST>
                <STBILLCATEGORIES.LIST></STBILLCATEGORIES.LIST>
              </BILLALLOCATIONS.LIST>";

        var ledgersXml = new StringBuilder();
        ledgersXml.Append($@"
            <LEDGERENTRIES.LIST>
              <OLDAUDITENTRYIDS.LIST TYPE=""Number""><OLDAUDITENTRYIDS>-1</OLDAUDITENTRYIDS></OLDAUDITENTRYIDS.LIST>
              <LEDGERNAME>{Escape(invoice.PartyLedgerName)}</LEDGERNAME>
              <ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE>
              <LEDGERFROMITEM>No</LEDGERFROMITEM>
              <REMOVEZEROENTRIES>No</REMOVEZEROENTRIES>
              <ISPARTYLEDGER>Yes</ISPARTYLEDGER>
              <GSTOVERRIDDEN>No</GSTOVERRIDDEN>
              <ISLASTDEEMEDPOSITIVE>Yes</ISLASTDEEMEDPOSITIVE>
              <AMOUNT>-{partyAmount:F3}</AMOUNT>
              <SERVICETATXDETAILS.LIST></SERVICETATXDETAILS.LIST>
              <BANKALLOCATION.LIST></BANKALLOCATION.LIST>
              {billAllocXml}
              <INTERESTCOLLECTION.LIST></INTERESTCOLLECTION.LIST>
              <OLDAUDITENTRIES.LIST></OLDAUDITENTRIES.LIST>
              <ACCOUNTAUDITENTRIES.LIST></ACCOUNTAUDITENTRIES.LIST>
              <AUDITENTRIES.LIST></AUDITENTRIES.LIST>
              <INPUTCRALOCS.LIST></INPUTCRALOCS.LIST>
              <DUTYHEADDETAILS.LIST></DUTYHEADDETAILS.LIST>
              <RATEDETAILS.LIST></RATEDETAILS.LIST>
              <REFVOUCHERDETAILS.LIST></REFVOUCHERDETAILS.LIST>
              <INVOICEWISEDETAILS.LIST></INVOICEWISEDETAILS.LIST>
              <TAXTYPEALLOCATIONS.LIST></TAXTYPEALLOCATIONS.LIST>
            </LEDGERENTRIES.LIST>");

        // Every other ledgerEntries line (VAT, discount, freight, round-off,
        // whatever the client's API decided applies) — generic, no line
        // "type" to special-case. Sign convention mirrors the party entry
        // above: a positive amount is a credit line (ISDEEMEDPOSITIVE=No,
        // added to what the party owes — e.g. VAT payable); a negative
        // amount (e.g. a discount) is a debit line (ISDEEMEDPOSITIVE=Yes).
        foreach (var entry in invoice.LedgerEntries)
            ledgersXml.Append(GenericLedgerEntry(entry.LedgerName, entry.Amount));

        var partyTrn      = invoice.PartyTRN?.Trim() ?? "";
        var partyState    = invoice.PartyState?.Trim() ?? "";
        var partyCountry  = invoice.PartyCountry?.Trim() ?? "";
        var voucherClass  = string.IsNullOrWhiteSpace(invoice.VoucherClass) ? defaultVoucherClass : invoice.VoucherClass;

        // Reconciled against two real exported "Sales AY TAX" vouchers —
        // this UAE VAT localisation does NOT use PARTYGSTIN/CONSIGNEEGSTIN
        // (an India/GST-only pair, never present in either sample) or a
        // state-level PLACEOFSUPPLY. The TRN instead goes into
        // TRADERCONSVATTINNO + BASICBUYERSSALESTAXNO (both hold the same
        // value in every sample), and PLACEOFSUPPLYCOUNTRY (not
        // PLACEOFSUPPLY) carries the country. GSTREGISTRATIONTYPE and
        // VATDEALERTYPE values below are copied verbatim from what real
        // registered vs. unregistered/cash-sale parties actually show.
        // PartyEmirate still isn't mapped anywhere — every sample shows
        // EMIRATEPOS as an unset "Not Applicable" placeholder even for a
        // Dubai party with PartyState=PartyEmirate="Dubai", so state
        // appears to be what actually drives location and Emirate is left
        // unused pending evidence otherwise.
        var hasTrn = !string.IsNullOrEmpty(partyTrn);
        var gstRegistrationType = hasTrn ? "Unknown" : "Unregistered/Consumer";
        var vatDealerType       = hasTrn ? "Regular"  : "Unregistered";

        var partyAddressLines = (invoice.PartyAddress ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var addressXml = partyAddressLines.Length == 0 ? "" : $@"
            <ADDRESS.LIST TYPE=""String"">{string.Concat(partyAddressLines.Select(a => $@"
              <ADDRESS>{Escape(a)}</ADDRESS>"))}
            </ADDRESS.LIST>
            <BASICBUYERADDRESS.LIST TYPE=""String"">{string.Concat(partyAddressLines.Select(a => $@"
              <BASICBUYERADDRESS>{Escape(a)}</BASICBUYERADDRESS>"))}
            </BASICBUYERADDRESS.LIST>";

        var xml = $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Import Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <IMPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Vouchers</REPORTNAME>
        <STATICVARIABLES>
          <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
        </STATICVARIABLES>
      </REQUESTDESC>
      <REQUESTDATA>
        <TALLYMESSAGE xmlns:UDF=""TallyUDF"">
          <VOUCHER REMOTEID=""{remoteId}"" VCHTYPE=""{Escape(voucherType)}"" ACTION=""Create"" OBJVIEW=""Invoice Voucher View"">{addressXml}
            <DATE>{dateStr}</DATE>
            <EFFECTIVEDATE>{dateStr}</EFFECTIVEDATE>
            <NARRATION>{Escape(narration)}</NARRATION>
            <OBJECTUPDATEACTION>Create</OBJECTUPDATEACTION>
            {(string.IsNullOrEmpty(partyCountry) ? "" : $"<COUNTRYOFRESIDENCE>{Escape(partyCountry)}</COUNTRYOFRESIDENCE>")}
            <GSTREGISTRATIONTYPE>{gstRegistrationType}</GSTREGISTRATIONTYPE>
            <VATDEALERTYPE>{vatDealerType}</VATDEALERTYPE>
            {(string.IsNullOrEmpty(partyState) ? "" : $"<STATENAME>{Escape(partyState)}</STATENAME>")}
            {(string.IsNullOrEmpty(partyTrn) ? "" : $@"<TRADERCONSVATTINNO>{Escape(partyTrn)}</TRADERCONSVATTINNO>
            <BASICBUYERSSALESTAXNO>{Escape(partyTrn)}</BASICBUYERSSALESTAXNO>")}
            {(string.IsNullOrEmpty(partyCountry) ? "" : $"<PLACEOFSUPPLYCOUNTRY>{Escape(partyCountry)}</PLACEOFSUPPLYCOUNTRY>")}
            {(string.IsNullOrEmpty(partyCountry) ? "" : $"<CONSIGNEECOUNTRYNAME>{Escape(partyCountry)}</CONSIGNEECOUNTRYNAME>")}
            {(string.IsNullOrEmpty(partyState) ? "" : $"<CONSIGNEESTATENAME>{Escape(partyState)}</CONSIGNEESTATENAME>")}
            <VOUCHERTYPENAME>{Escape(voucherType)}</VOUCHERTYPENAME>
            {(string.IsNullOrEmpty(voucherClass) ? "" : $"<CLASSNAME>{Escape(voucherClass)}</CLASSNAME>")}
            <PARTYNAME>{Escape(invoice.PartyLedgerName)}</PARTYNAME>
            <PARTYLEDGERNAME>{Escape(invoice.PartyLedgerName)}</PARTYLEDGERNAME>
            <VOUCHERNUMBER>{Escape(invoice.InvoiceNo)}</VOUCHERNUMBER>
            <PARTYMAILINGNAME>{Escape(invoice.PartyLedgerName)}</PARTYMAILINGNAME>
            <BASICBUYERNAME>{Escape(invoice.PartyLedgerName)}</BASICBUYERNAME>
            <REFERENCE>{Escape(invoice.InvoiceNo)}</REFERENCE>
            <ISINVOICE>Yes</ISINVOICE>
            <ISOPTIONAL>{(isOptional ? "Yes" : "No")}</ISOPTIONAL>
            <HASDISCOUNTS>No</HASDISCOUNTS>
            {string.Concat(invoice.Udf.Select(kv => $@"
            <UDF:{kv.Key}>{Escape(kv.Value)}</UDF:{kv.Key}>"))}
            {itemsXml}
            {ledgersXml}
            <CONTRITRANS.LIST></CONTRITRANS.LIST>
            <GST.LIST></GST.LIST>
            <PAYROLLMODEOFPAYMENT.LIST></PAYROLLMODEOFPAYMENT.LIST>
          </VOUCHER>
        </TALLYMESSAGE>
      </REQUESTDATA>
    </IMPORTDATA>
  </BODY>
</ENVELOPE>";

        // Full request XML logged unconditionally while diagnosing pushes
        // that fail with no LINEERROR (a bare ERRORS=1) — the request is
        // the other half of that picture and the log has no other record
        // of exactly what was sent for a given invoice.
        _logger.Info($"Tally request for {invoice.InvoiceNo}:\n{xml}");

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return (false, "Could not connect to Tally");

        var rawResponse = doc.ToString();
        _logger.Info($"Tally response for {invoice.InvoiceNo}:\n{rawResponse}");

        var lineError = doc.Descendants("LINEERROR").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(lineError)) return (false, lineError);

        if (doc.Descendants("CREATED").FirstOrDefault()?.Value == "1") return (true, "Synced");
        if (doc.Descendants("ALTERED").FirstOrDefault()?.Value == "1") return (true, "Updated");

        // ERRORS>0 with no LINEERROR — Tally rejected the whole import
        // without per-line detail. Seen so far on a retried push of an
        // invoice/REMOTEID that already failed once before (this agent
        // has no local DB, so it can't tell a genuinely-new invoice from
        // one the API is still returning because ReportPushResultAsync is
        // currently disabled — see SyncOrchestrator). The full request/
        // response above are the only way to tell that apart from a new
        // structural XML problem until that's confirmed either way.
        return (false, rawResponse.Length > 500 ? rawResponse[..500] + "…" : rawResponse);
    }

    private static string GenericLedgerEntry(string ledgerName, decimal amount) => $@"
            <LEDGERENTRIES.LIST>
              <OLDAUDITENTRYIDS.LIST TYPE=""Number""><OLDAUDITENTRYIDS>-1</OLDAUDITENTRYIDS></OLDAUDITENTRYIDS.LIST>
              <LEDGERNAME>{Escape(ledgerName)}</LEDGERNAME>
              <ISDEEMEDPOSITIVE>{(amount >= 0 ? "No" : "Yes")}</ISDEEMEDPOSITIVE>
              <LEDGERFROMITEM>No</LEDGERFROMITEM>
              <REMOVEZEROENTRIES>No</REMOVEZEROENTRIES>
              <ISPARTYLEDGER>No</ISPARTYLEDGER>
              <GSTOVERRIDDEN>No</GSTOVERRIDDEN>
              <AMOUNT>{amount:F3}</AMOUNT>
              <VATEXPAMOUNT>{amount:F3}</VATEXPAMOUNT>
              <SERVICETATXDETAILS.LIST></SERVICETATXDETAILS.LIST>
              <BANKALLOCATION.LIST></BANKALLOCATION.LIST>
              <BILLALLOCATIONS.LIST></BILLALLOCATIONS.LIST>
              <INTERESTCOLLECTION.LIST></INTERESTCOLLECTION.LIST>
              <OLDAUDITENTRIES.LIST></OLDAUDITENTRIES.LIST>
              <ACCOUNTAUDITENTRIES.LIST></ACCOUNTAUDITENTRIES.LIST>
              <AUDITENTRIES.LIST></AUDITENTRIES.LIST>
              <INPUTCRALOCS.LIST></INPUTCRALOCS.LIST>
              <DUTYHEADDETAILS.LIST></DUTYHEADDETAILS.LIST>
              <RATEDETAILS.LIST></RATEDETAILS.LIST>
              <REFVOUCHERDETAILS.LIST></REFVOUCHERDETAILS.LIST>
              <INVOICEWISEDETAILS.LIST></INVOICEWISEDETAILS.LIST>
              <TAXTYPEALLOCATIONS.LIST></TAXTYPEALLOCATIONS.LIST>
            </LEDGERENTRIES.LIST>";

    private static readonly Regex LiteralInvalidXmlChars =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    private static readonly Regex XmlNumericCharRef =
        new(@"&#(?:x([0-9a-fA-F]+)|([0-9]+));", RegexOptions.Compiled);

    // Vouchers on a Voucher Type with User Defined Fields configured get their
    // UDF values wrapped in <UDF:...> tags in Tally's XML export, but Tally
    // never declares the "UDF" namespace prefix on the root <ENVELOPE> —
    // XDocument.Parse then throws "'UDF' is an undeclared prefix" for any
    // response that happens to include such a voucher. We don't read UDF data
    // at all, so it's enough to just declare the prefix so the doc parses.
    private static string DeclareUdfNamespace(string xml) =>
        xml.Contains("UDF:") && !xml.Contains("xmlns:UDF")
            ? xml.Replace("<ENVELOPE>", "<ENVELOPE xmlns:UDF=\"TallyUDF\">")
            : xml;

    private static string StripInvalidXmlChars(string xml)
    {
        xml = XmlNumericCharRef.Replace(xml, m =>
        {
            int cp = m.Groups[1].Success
                ? Convert.ToInt32(m.Groups[1].Value, 16)
                : int.Parse(m.Groups[2].Value);
            bool valid = cp is 0x9 or 0xA or 0xD
                      || (cp >= 0x20  && cp <= 0xD7FF)
                      || (cp >= 0xE000 && cp <= 0xFFFD)
                      || cp >= 0x10000;
            return valid ? m.Value : string.Empty;
        });

        return LiteralInvalidXmlChars.Replace(xml, string.Empty);
    }

    private static string Escape(string val) => System.Security.SecurityElement.Escape(val) ?? val;
}
