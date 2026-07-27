using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SaleOrd.SyncAgent.Models;

namespace SaleOrd.SyncAgent.Services;

// Ported from the SaleOrd web app's Services/TallyService.cs — same Tally
// XML query/import logic, unchanged, just re-homed here since this agent
// (not the web app) is the one with LAN access to Tally when
// TallySync:Mode = Agent. Keep this in sync with the web app's version when
// either one gets a Tally-schema fix; they will drift in practice like
// MasterSyncJob and SyncController already do elsewhere in this codebase.
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
            return XDocument.Parse(StripInvalidXmlChars(body));
        }
        catch (Exception ex)
        {
            _logger.Error($"Tally XML request failed for {url}", ex);
            return null;
        }
    }

    public async Task<List<Ledger>> GetLedgersAsync(string tallyUrl, Company company)
    {
        var xml = $@"<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Ledger</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>{Escape(company.TallyCompanyName)}</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""List of Ledger"" ISINITIALIZE=""Yes"">
                        <TYPE>Ledger</TYPE>
                        <FETCH>Name,Parent,_Address1,_Address2,PriorStateName,LedgerContact,LedgerPhone,Email,
                               SalesTaxNumber,IncomeTaxNumber,VATTINNUMBER,TaxType,LedgerFax,
                               OpeningBalance,ClosingBalance,CreditLimit,PartyGSTIN,GUID,AlterId</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        var now = DateTime.Now;
        return doc.Descendants("LEDGER")
            .Select(el => new Ledger
            {
                LedgerName     = GetName(el),
                Parent         = el.Element("PARENT")?.Value?.Trim() ?? string.Empty,
                Address        = string.Join(", ", new[] {
                                     el.Element("_ADDRESS1")?.Value,
                                     el.Element("_ADDRESS2")?.Value }
                                     .Where(v => !string.IsNullOrWhiteSpace(v))!),
                State          = el.Element("PRIORSTATENAME")?.Value?.Trim(),
                MobileNo       = el.Element("LEDGERPHONE")?.Value?.Trim()
                              ?? el.Element("LEDGERCONTACT")?.Value?.Trim(),
                Email          = el.Element("EMAIL")?.Value?.Trim(),
                LedgerFax      = el.Element("LEDGERFAX")?.Value?.Trim(),
                GSTNo          = el.Element("PARTYGSTIN")?.Value?.Trim(),
                TaxType        = el.Element("TAXTYPE")?.Value?.Trim(),
                IncomeTaxNo    = el.Element("INCOMETAXNUMBER")?.Value?.Trim(),
                VATTINNo       = el.Element("VATTINNUMBER")?.Value?.Trim(),
                CreditLimit    = ParseDecimal(el.Element("CREDITLIMIT")?.Value),
                OpeningBalance = ParseDecimal(el.Element("OPENINGBALANCE")?.Value),
                ClosingBalance = ParseDecimal(el.Element("CLOSINGBALANCE")?.Value),
                GUID           = el.Element("GUID")?.Value?.Trim(),
                AlterId        = ParseLong(el.Element("ALTERID")?.Value),
                CompanyId      = company.CompanyId,
                LastSyncedAt   = now
            })
            .Where(l => !string.IsNullOrWhiteSpace(l.LedgerName))
            .ToList();
    }

    public async Task<List<StockItem>> GetStockItemsAsync(string tallyUrl, Company company)
    {
        var xml = $@"<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Stock Items</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>{Escape(company.TallyCompanyName)}</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""List of Stock Items"" ISINITIALIZE=""Yes"">
                        <TYPE>Stock Item</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,BaseUnits,AdditionalUnits,
                               _Conversion,RateOfDuty,OpeningBalance,ClosingBalance,
                               OpeningValue,ClosingValue,IsBatchwiseOn,IsCostTrackingOn</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        var now = DateTime.Now;
        return doc.Descendants("STOCKITEM")
            .Select(el => new StockItem
            {
                ItemName        = GetName(el),
                Parent          = el.Element("PARENT")?.Value?.Trim() ?? string.Empty,
                UOM             = el.Element("BASEUNITS")?.Value?.Trim() ?? string.Empty,
                AdditionalUnits = el.Element("ADDITIONALUNITS")?.Value?.Trim(),
                RateOfDuty      = ParseDecimal(el.Element("RATEOFDUTY")?.Value),
                OpeningBalance  = ParseDecimal(el.Element("OPENINGBALANCE")?.Value),
                ClosingBalance  = ParseDecimal(el.Element("CLOSINGBALANCE")?.Value),
                OpeningValue    = ParseDecimal(el.Element("OPENINGVALUE")?.Value),
                ClosingValue    = ParseDecimal(el.Element("CLOSINGVALUE")?.Value),
                IsBatchwiseOn   = el.Element("ISBATCHWISEON")?.Value == "Yes",
                IsCostTrackingOn= el.Element("ISCOSTTRACKINGON")?.Value == "Yes",
                GUID            = el.Element("GUID")?.Value?.Trim(),
                AlterId         = ParseLong(el.Element("ALTERID")?.Value),
                CompanyId       = company.CompanyId,
                LastSyncedAt    = now
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.ItemName))
            .ToList();
    }

    public async Task<List<Godown>> GetGodownsAsync(string tallyUrl, Company company)
    {
        var xml = $@"<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Godowns</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>{Escape(company.TallyCompanyName)}</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""List of Godowns"" ISINITIALIZE=""Yes"">
                        <TYPE>Godown</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId,IsBatchesOn,Address,City,State,PinCode</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        var now = DateTime.Now;
        return doc.Descendants("GODOWN")
            .Select(el => new Godown
            {
                GodownName    = GetName(el),
                Parent        = el.Element("PARENT")?.Value?.Trim(),
                Address       = el.Element("ADDRESS")?.Value?.Trim(),
                City          = el.Element("CITY")?.Value?.Trim(),
                State         = el.Element("STATE")?.Value?.Trim(),
                PinCode       = el.Element("PINCODE")?.Value?.Trim(),
                IsBatchwiseOn = el.Element("ISBATCHESON")?.Value == "Yes",
                GUID          = el.Element("GUID")?.Value?.Trim(),
                AlterId       = ParseLong(el.Element("ALTERID")?.Value),
                CompanyId     = company.CompanyId,
                LastSyncedAt  = now
            })
            .Where(g => !string.IsNullOrWhiteSpace(g.GodownName))
            .ToList();
    }

    // Bounded to the last 180 days — an unbounded full-history scan here was
    // found to crash Tally's native HTTP engine on a client install with a
    // long sales history (see GetRecentSalesInvoicesAsync). No EXPLODEVCHTYPE
    // either: it forces full voucher-type-class resolution, which a generic
    // field like AllInventoryEntries doesn't need — confirmed unnecessary by
    // comparing against LoheBgService, a reference project that reliably
    // handles 200-300 Tally records per run without it and never crashes.
    public async Task<List<SaleRateRecord>> GetLastSaleRatesAsync(string tallyUrl, Company company)
    {
        var fromDate = DateTime.Today.AddDays(-180);
        var xml = $@"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><TALLYREQUEST>EXPORT</TALLYREQUEST><TYPE>COLLECTION</TYPE><ID>List of Sale Rates</ID></HEADER>
  <BODY><DESC>
    <STATICVARIABLES>
      <SVCURRENTCOMPANY>{Escape(company.TallyCompanyName)}</SVCURRENTCOMPANY>
      <SVFROMDATE TYPE=""Date"">{fromDate:yyyyMMdd}</SVFROMDATE>
      <SVTODATE TYPE=""Date"">{DateTime.Today:yyyyMMdd}</SVTODATE>
      <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
    </STATICVARIABLES>
    <TDL><TDLMESSAGE>
      <COLLECTION NAME=""List of Sale Rates"" ISINITIALIZE=""Yes"">
        <TYPE>Voucher</TYPE>
        <FILTER>IsSales</FILTER>
        <FETCH>Date,PartyLedgerName,AllInventoryEntries.List:StockItemName,AllInventoryEntries.List:Rate</FETCH>
      </COLLECTION>
      <SYSTEM TYPE=""Formulae"" NAME=""IsSales"">$$IsEqual:$VoucherTypeName:&quot;Sales&quot;</SYSTEM>
    </TDLMESSAGE></TDL>
  </DESC></BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        var latest = new Dictionary<(string Party, string Item), SaleRateRecord>();

        foreach (var vch in doc.Descendants("VOUCHER"))
        {
            var partyName = vch.Element("PARTYLEDGERNAME")?.Value?.Trim();
            var dateVal = vch.Element("DATE")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(partyName) || dateVal?.Length != 8) continue;
            if (!DateTime.TryParseExact(dateVal, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var saleDate))
                continue;

            foreach (var entry in vch.Descendants("ALLINVENTORYENTRIES.LIST"))
            {
                var itemName = entry.Element("STOCKITEMNAME")?.Value?.Trim();
                var rate = ParseRate(entry.Element("RATE")?.Value);
                if (string.IsNullOrWhiteSpace(itemName) || rate <= 0) continue;

                var key = (partyName, itemName);
                if (!latest.TryGetValue(key, out var existing) || saleDate > existing.SaleDate)
                {
                    latest[key] = new SaleRateRecord
                    {
                        PartyName = partyName,
                        ItemName = itemName,
                        Rate = rate,
                        SaleDate = saleDate
                    };
                }
            }
        }

        return latest.Values.ToList();
    }

    public async Task<(bool Success, string Message)> PushSaleOrderAsync(
        string tallyUrl, SaleOrder order, string companyName,
        string salesLedger  = "Sales",
        string igstLedger   = "IGST",
        string cgstLedger   = "CGST",
        string sgstLedger   = "SGST",
        string roundOffLedger = "Round Off",
        bool   isAlter      = false,
        string voucherType  = "Sales Order",
        string batchName    = "Primary Batch")
    {
        var action     = isAlter ? "Alter" : "Create";
        var dateStr    = order.OrderDate.ToString("yyyyMMdd");
        var grandTotal = order.GrandTotal > 0 ? order.GrandTotal : order.TotalAmount;
        var narration  = string.IsNullOrWhiteSpace(order.Narration)
            ? $"Ref: {order.OrderNo}"
            : $"Ref: {order.OrderNo} | {order.Narration}";
        var remoteId   = $"saleord{order.SaleOrderId:D7}";
        var dueDate    = order.DeliveryDate ?? order.OrderDate;

        var itemsXml = new StringBuilder();
        foreach (var item in order.Items)
        {
            var rateStr = $"{item.Rate:F2}/{item.UOM}";
            var qtyStr  = $" {item.Qty:F3} {item.UOM}";

            var batchXml = string.Empty;
            if (!string.IsNullOrWhiteSpace(item.GodownName))
            {
                var jd = TallyJd(dueDate);
                var p  = dueDate.ToString("d-MMM-yy");
                batchXml = $@"
              <BATCHALLOCATIONS.LIST>
                <GODOWNNAME>{Escape(item.GodownName)}</GODOWNNAME>
                <BATCHNAME>{Escape(batchName)}</BATCHNAME>
                <ORDERNO>{Escape(order.OrderNo)}</ORDERNO>
                <AMOUNT>{item.Amount:F2}</AMOUNT>
                <ACTUALQTY>{qtyStr}</ACTUALQTY>
                <BILLEDQTY>{qtyStr}</BILLEDQTY>
                <ORDERDUEDATE JD=""{jd}"" P=""{p}"">{p}</ORDERDUEDATE>
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
              <LEDGERNAME>{Escape(order.LedgerName)}</LEDGERNAME>
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

        if (order.TaxType == "IGST" && order.IGSTTotal > 0)
        {
            ledgersXml.Append(TaxLedgerEntry(igstLedger, order.IGSTTotal));
        }
        else if (order.TaxType == "CGST_SGST")
        {
            if (order.CGSTTotal > 0) ledgersXml.Append(TaxLedgerEntry(cgstLedger, order.CGSTTotal));
            if (order.SGSTTotal > 0) ledgersXml.Append(TaxLedgerEntry(sgstLedger, order.SGSTTotal));
        }

        if (order.RoundOff != 0)
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
              <AMOUNT>{order.RoundOff:F2}</AMOUNT>
              <VATEXPAMOUNT>{order.RoundOff:F2}</VATEXPAMOUNT>
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

        var partyGstin  = order.Ledger?.GSTNo?.Trim() ?? "";
        var partyState  = order.Ledger?.State?.Trim() ?? "";
        var partyAddressLines = (order.Ledger?.Address ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var addressXml = partyAddressLines.Length == 0 ? "" : $@"
            <ADDRESS.LIST TYPE=""String"">{string.Concat(partyAddressLines.Select(a => $@"
              <ADDRESS>{Escape(a)}</ADDRESS>"))}
            </ADDRESS.LIST>
            <BASICBUYERADDRESS.LIST TYPE=""String"">{string.Concat(partyAddressLines.Select(a => $@"
              <BASICBUYERADDRESS>{Escape(a)}</BASICBUYERADDRESS>"))}
            </BASICBUYERADDRESS.LIST>";
        var gstRegistrationType = string.IsNullOrEmpty(partyGstin) ? "Unregistered" : "Regular";
        var hasDiscounts = order.Items.Any(i => i.Discount > 0) ? "Yes" : "No";

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
            <VOUCHERTYPENAME>{Escape(voucherType)}</VOUCHERTYPENAME>
            <PARTYNAME>{Escape(order.LedgerName)}</PARTYNAME>
            <PARTYLEDGERNAME>{Escape(order.LedgerName)}</PARTYLEDGERNAME>
            <PARTYMAILINGNAME>{Escape(order.LedgerName)}</PARTYMAILINGNAME>
            <BASICBUYERNAME>{Escape(order.LedgerName)}</BASICBUYERNAME>
            <REFERENCE>{Escape(order.OrderNo)}</REFERENCE>
            <ISINVOICE>No</ISINVOICE>
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

    private static int TallyJd(DateTime date) =>
        (int)(date.Date - new DateTime(1899, 12, 30)).TotalDays;

    public class InvoiceLookupRecord
    {
        public string? InvoiceNo { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public List<string> OrderRefs { get; set; } = new();
    }

    // Fetches Sales vouchers in [fromDate, today] ONCE, so a whole batch of
    // pending orders can be matched against a single Tally round-trip.
    //
    // An earlier per-order version of this query had no date bound at all —
    // querying Tally's entire Sales voucher history, once for EVERY
    // uninvoiced order, every cycle. On a client install with several years
    // of sales history that was enough to crash Tally's native HTTP engine
    // outright (STATUS_ACCESS_VIOLATION / c0000005). Bounding by date and
    // fetching once per company per cycle fixes both the crash and the
    // wasted repeated full-history scans. See REFERENCE's comment further
    // down for why that flat field is matched ahead of the nested
    // INVOICEORDERLIST.LIST.
    //
    // No EXPLODEVCHTYPE: comparing against LoheBgService (a reference project
    // that reliably imports/reads 200-300 Tally records per run without ever
    // crashing Tally) showed its only EXPORT query is a single exact-name-
    // filtered ledger lookup, never a bulk voucher scan with that flag. Both
    // REFERENCE and INVOICEORDERLIST.LIST are generic Voucher fields present
    // regardless of voucher-type class, so full type-hierarchy resolution
    // was unnecessary overhead here, not a requirement.
    public async Task<List<InvoiceLookupRecord>> GetRecentSalesInvoicesAsync(
        string tallyUrl, string companyName, DateTime fromDate)
    {
        var xml = $@"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><TALLYREQUEST>EXPORT</TALLYREQUEST><TYPE>COLLECTION</TYPE><ID>InvCheck</ID></HEADER>
  <BODY><DESC>
    <STATICVARIABLES>
      <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
      <SVFROMDATE TYPE=""Date"">{fromDate:yyyyMMdd}</SVFROMDATE>
      <SVTODATE TYPE=""Date"">{DateTime.Today:yyyyMMdd}</SVTODATE>
      <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
    </STATICVARIABLES>
    <TDL><TDLMESSAGE>
      <COLLECTION NAME=""InvCheck"" ISINITIALIZE=""Yes"">
        <TYPE>Voucher</TYPE>
        <FILTER>IsSales</FILTER>
        <FETCH>VoucherNumber,Date,VoucherTypeName,Reference,InvoiceOrderList.List:BasicPurchaseOrderNo</FETCH>
      </COLLECTION>
      <SYSTEM TYPE=""Formulae"" NAME=""IsSales"">$$IsEqual:$VoucherTypeName:&quot;Sales&quot;</SYSTEM>
    </TDLMESSAGE></TDL>
  </DESC></BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        var results = new List<InvoiceLookupRecord>();
        foreach (var vch in doc.Descendants("VOUCHER"))
        {
            // REFERENCE (flat field, Tally auto-fills it with the order number
            // when an invoice is raised "against" an order) fetches reliably;
            // INVOICEORDERLIST.LIST (nested list) is kept only as a fallback.
            var reference = vch.Element("REFERENCE")?.Value?.Trim();
            var orderRefs = vch.Descendants("INVOICEORDERLIST.LIST")
                .Select(ol => ol.Element("BASICPURCHASEORDERNO")?.Value?.Trim())
                .Append(reference)
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (orderRefs.Count == 0) continue;

            var dateVal = vch.Element("DATE")?.Value?.Trim();
            DateTime? invDate = null;
            if (dateVal?.Length == 8 &&
                DateTime.TryParseExact(dateVal, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var d))
                invDate = d;

            results.Add(new InvoiceLookupRecord
            {
                InvoiceNo = vch.Element("VOUCHERNUMBER")?.Value?.Trim(),
                InvoiceDate = invDate,
                OrderRefs = orderRefs
            });
        }
        return results;
    }

    // Matches one order number against an already-fetched invoice list —
    // call GetRecentSalesInvoicesAsync once per company per cycle, then this
    // per order, instead of hitting Tally again for every order.
    public static bool TryMatchInvoice(
        List<InvoiceLookupRecord> invoices, string orderNo,
        out string? invoiceNo, out DateTime? invoiceDate)
    {
        var match = invoices.FirstOrDefault(inv =>
            inv.OrderRefs.Any(r => string.Equals(r, orderNo, StringComparison.OrdinalIgnoreCase)));
        invoiceNo = match?.InvoiceNo;
        invoiceDate = match?.InvoiceDate;
        return match != null;
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

    private static string GetName(XElement el) =>
        el.Attribute("NAME")?.Value?.Trim()
        ?? el.Element("NAME")?.Value?.Trim()
        ?? el.Value?.Trim()
        ?? string.Empty;

    private static readonly Regex LiteralInvalidXmlChars =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    private static readonly Regex XmlNumericCharRef =
        new(@"&#(?:x([0-9a-fA-F]+)|([0-9]+));", RegexOptions.Compiled);

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

    private static long ParseLong(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0;
        return long.TryParse(val.Trim(), out var l) ? l : 0;
    }

    private static decimal ParseDecimal(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0;
        val = val.Replace(" Dr", "").Replace(" Cr", "").Trim();
        return decimal.TryParse(val, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static decimal ParseRate(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0;
        var slashIdx = val.IndexOf('/');
        if (slashIdx > 0) val = val[..slashIdx];
        return ParseDecimal(val);
    }
}
