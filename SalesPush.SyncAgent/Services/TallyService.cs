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

    // Pushes one SaleInvoice into Tally as a Sales Invoice voucher (ISINVOICE
    // = Yes, unlike SaleOrd.SyncAgent's Sales Order push). isAlter re-pushes
    // an already-created voucher (e.g. the API sent a corrected version) as
    // an ACTION="Alter" instead of "Create".
    public async Task<(bool Success, string Message)> PushSalesInvoiceAsync(
        string tallyUrl, SaleInvoice invoice, string companyName,
        string salesLedger    = "Sales",
        string igstLedger     = "IGST",
        string cgstLedger     = "CGST",
        string sgstLedger     = "SGST",
        string roundOffLedger = "Round Off",
        bool   isAlter        = false,
        string voucherType    = "Sales",
        string batchName      = "Primary Batch")
    {
        var action     = isAlter ? "Alter" : "Create";
        var dateStr    = invoice.InvoiceDate.ToString("yyyyMMdd");
        var grandTotal = invoice.GrandTotal > 0 ? invoice.GrandTotal : invoice.TotalAmount;
        var narration  = string.IsNullOrWhiteSpace(invoice.Narration)
            ? $"Ref: {invoice.InvoiceNo}"
            : $"Ref: {invoice.InvoiceNo} | {invoice.Narration}";
        var remoteId   = $"salespush{invoice.SaleInvoiceId:D7}";

        var itemsXml = new StringBuilder();
        foreach (var item in invoice.Items)
        {
            var rateStr = $"{item.Rate:F2}/{item.UOM}";
            var qtyStr  = $" {item.Qty:F3} {item.UOM}";

            var batchXml = string.Empty;
            if (!string.IsNullOrWhiteSpace(item.GodownName))
            {
                batchXml = $@"
              <BATCHALLOCATIONS.LIST>
                <GODOWNNAME>{Escape(item.GodownName)}</GODOWNNAME>
                <BATCHNAME>{Escape(batchName)}</BATCHNAME>
                <AMOUNT>{item.Amount:F2}</AMOUNT>
                <ACTUALQTY>{qtyStr}</ACTUALQTY>
                <BILLEDQTY>{qtyStr}</BILLEDQTY>
                <ADDITIONALDETAILS.LIST></ADDITIONALDETAILS.LIST>
                <VOUCHERCOMPONENTLIST.LIST></VOUCHERCOMPONENTLIST.LIST>
              </BATCHALLOCATIONS.LIST>";
            }

            itemsXml.Append($@"
            <ALLINVENTORYENTRIES.LIST>
              <STOCKITEMNAME>{Escape(item.ItemName)}</STOCKITEMNAME>
              <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
              <RATE>{rateStr}</RATE>
              <DISCOUNT>{item.Discount:F2}</DISCOUNT>
              <AMOUNT>{item.Amount:F2}</AMOUNT>
              <ACTUALQTY>{qtyStr}</ACTUALQTY>
              <BILLEDQTY>{qtyStr}</BILLEDQTY>
              {batchXml}
              <ACCOUNTINGALLOCATIONS.LIST>
                <LEDGERNAME>{Escape(salesLedger)}</LEDGERNAME>
                <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
                <LEDGERFROMITEM>No</LEDGERFROMITEM>
                <REMOVEZEROENTRIES>No</REMOVEZEROENTRIES>
                <ISPARTYLEDGER>No</ISPARTYLEDGER>
                <GSTOVERRIDDEN>No</GSTOVERRIDDEN>
                <AMOUNT>{item.Amount:F2}</AMOUNT>
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
              </ACCOUNTINGALLOCATIONS.LIST>
              <DUTYHEADDETAILS.LIST></DUTYHEADDETAILS.LIST>
              <RATEDETAILS.LIST></RATEDETAILS.LIST>
              <SUPPLEMENTARYDUTYHEADDETAILS.LIST></SUPPLEMENTARYDUTYHEADDETAILS.LIST>
              <TAXOBJECTALLOCATIONS.LIST></TAXOBJECTALLOCATIONS.LIST>
              <REFVOUCHERDETAILS.LIST></REFVOUCHERDETAILS.LIST>
              <EXCISEALLOCATIONS.LIST></EXCISEALLOCATIONS.LIST>
              <EXPENSEALLOCATIONS.LIST></EXPENSEALLOCATIONS.LIST>
            </ALLINVENTORYENTRIES.LIST>");
        }

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
              <AMOUNT>-{grandTotal:F2}</AMOUNT>
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
            </LEDGERENTRIES.LIST>");

        if (invoice.TaxType == "IGST" && invoice.IGSTTotal > 0)
        {
            ledgersXml.Append(TaxLedgerEntry(igstLedger, invoice.IGSTTotal));
        }
        else if (invoice.TaxType == "CGST_SGST")
        {
            if (invoice.CGSTTotal > 0) ledgersXml.Append(TaxLedgerEntry(cgstLedger, invoice.CGSTTotal));
            if (invoice.SGSTTotal > 0) ledgersXml.Append(TaxLedgerEntry(sgstLedger, invoice.SGSTTotal));
        }

        if (invoice.RoundOff != 0)
        {
            ledgersXml.Append($@"
            <LEDGERENTRIES.LIST>
              <OLDAUDITENTRYIDS.LIST TYPE=""Number""><OLDAUDITENTRYIDS>-1</OLDAUDITENTRYIDS></OLDAUDITENTRYIDS.LIST>
              <ROUNDTYPE>Normal Rounding</ROUNDTYPE>
              <LEDGERNAME>{Escape(roundOffLedger)}</LEDGERNAME>
              <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
              <LEDGERFROMITEM>No</LEDGERFROMITEM>
              <REMOVEZEROENTRIES>No</REMOVEZEROENTRIES>
              <ISPARTYLEDGER>No</ISPARTYLEDGER>
              <GSTOVERRIDDEN>No</GSTOVERRIDDEN>
              <ROUNDLIMIT> 1</ROUNDLIMIT>
              <AMOUNT>{invoice.RoundOff:F2}</AMOUNT>
              <VATEXPAMOUNT>{invoice.RoundOff:F2}</VATEXPAMOUNT>
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
            </LEDGERENTRIES.LIST>");
        }

        var partyGstin  = invoice.PartyGSTNo?.Trim() ?? "";
        var partyState  = invoice.PartyState?.Trim() ?? "";
        var partyCreditPeriod = invoice.PartyCreditPeriod?.Trim() ?? "";
        var partyAddressLines = (invoice.PartyAddress ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var addressXml = partyAddressLines.Length == 0 ? "" : $@"
            <ADDRESS.LIST TYPE=""String"">{string.Concat(partyAddressLines.Select(a => $@"
              <ADDRESS>{Escape(a)}</ADDRESS>"))}
            </ADDRESS.LIST>
            <BASICBUYERADDRESS.LIST TYPE=""String"">{string.Concat(partyAddressLines.Select(a => $@"
              <BASICBUYERADDRESS>{Escape(a)}</BASICBUYERADDRESS>"))}
            </BASICBUYERADDRESS.LIST>";
        var gstRegistrationType = string.IsNullOrEmpty(partyGstin) ? "Unregistered" : "Regular";
        var hasDiscounts = invoice.Items.Any(i => i.Discount > 0) ? "Yes" : "No";

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
          <VOUCHER REMOTEID=""{remoteId}"" VCHTYPE=""{Escape(voucherType)}"" ACTION=""{action}"" OBJVIEW=""Invoice Voucher View"">{addressXml}
            <DATE>{dateStr}</DATE>
            <EFFECTIVEDATE>{dateStr}</EFFECTIVEDATE>
            <NARRATION>{Escape(narration)}</NARRATION>
            <OBJECTUPDATEACTION>{action}</OBJECTUPDATEACTION>
            <COUNTRYOFRESIDENCE>India</COUNTRYOFRESIDENCE>
            {(string.IsNullOrEmpty(partyGstin) ? "" : $"<PARTYGSTIN>{Escape(partyGstin)}</PARTYGSTIN>")}
            {(string.IsNullOrEmpty(partyState) ? "" : $@"<STATENAME>{Escape(partyState)}</STATENAME>
            <PLACEOFSUPPLY>{Escape(partyState)}</PLACEOFSUPPLY>")}
            <GSTREGISTRATIONTYPE>{gstRegistrationType}</GSTREGISTRATIONTYPE>
            <CONSIGNEECOUNTRYNAME>India</CONSIGNEECOUNTRYNAME>
            {(string.IsNullOrEmpty(partyGstin) ? "" : $"<CONSIGNEEGSTIN>{Escape(partyGstin)}</CONSIGNEEGSTIN>")}
            {(string.IsNullOrEmpty(partyState) ? "" : $"<CONSIGNEESTATENAME>{Escape(partyState)}</CONSIGNEESTATENAME>")}
            {(string.IsNullOrEmpty(partyCreditPeriod) ? "" : $"<TERMSOFPAYMENT>{Escape(partyCreditPeriod)}</TERMSOFPAYMENT>")}
            <VOUCHERTYPENAME>{Escape(voucherType)}</VOUCHERTYPENAME>
            <PARTYNAME>{Escape(invoice.PartyLedgerName)}</PARTYNAME>
            <PARTYLEDGERNAME>{Escape(invoice.PartyLedgerName)}</PARTYLEDGERNAME>
            <PARTYMAILINGNAME>{Escape(invoice.PartyLedgerName)}</PARTYMAILINGNAME>
            <BASICBUYERNAME>{Escape(invoice.PartyLedgerName)}</BASICBUYERNAME>
            <REFERENCE>{Escape(invoice.InvoiceNo)}</REFERENCE>
            <ISINVOICE>Yes</ISINVOICE>
            <ISOPTIONAL>No</ISOPTIONAL>
            <HASDISCOUNTS>{hasDiscounts}</HASDISCOUNTS>
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

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return (false, "Could not connect to Tally");

        var lineError = doc.Descendants("LINEERROR").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(lineError)) return (false, lineError);

        if (doc.Descendants("CREATED").FirstOrDefault()?.Value == "1") return (true, "Synced");
        if (doc.Descendants("ALTERED").FirstOrDefault()?.Value == "1") return (true, "Updated");

        var raw = doc.ToString();
        return (false, raw[..Math.Min(300, raw.Length)]);
    }

    private static string TaxLedgerEntry(string ledgerName, decimal amount) => $@"
            <LEDGERENTRIES.LIST>
              <OLDAUDITENTRYIDS.LIST TYPE=""Number""><OLDAUDITENTRYIDS>-1</OLDAUDITENTRYIDS></OLDAUDITENTRYIDS.LIST>
              <LEDGERNAME>{Escape(ledgerName)}</LEDGERNAME>
              <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
              <LEDGERFROMITEM>No</LEDGERFROMITEM>
              <REMOVEZEROENTRIES>No</REMOVEZEROENTRIES>
              <ISPARTYLEDGER>No</ISPARTYLEDGER>
              <GSTOVERRIDDEN>No</GSTOVERRIDDEN>
              <AMOUNT>{amount:F2}</AMOUNT>
              <VATEXPAMOUNT>{amount:F2}</VATEXPAMOUNT>
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
