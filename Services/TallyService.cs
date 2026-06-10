using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SaleOrd.Models.Domain;

namespace SaleOrd.Services;

public class TallyService
{
    private readonly HttpClient _http;
    private readonly ILogger<TallyService> _logger;
    private readonly string _fallbackUrl;

    public TallyService(HttpClient http, ILogger<TallyService> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        var ip   = config["TallySettings:DefaultIp"]   ?? "localhost";
        var port = config.GetValue<int>("TallySettings:DefaultPort", 9000);
        _fallbackUrl = $"http://{ip}:{port}";
    }

    // Parse "http://host:port" or "host:port" → (host, port)
    public static (string Ip, int Port) ParseUrl(string? url)
    {
        try
        {
            var u = new Uri((url ?? "").Contains("://") ? url! : "http://" + url);
            return (u.Host, u.Port > 0 ? u.Port : 9000);
        }
        catch { return ("localhost", 9000); }
    }

    private async Task<XDocument?> PostXmlAsync(string url, string xml)
    {
        try
        {
            var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            var response = await _http.PostAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Tally response ({Len} chars): {Body}", body.Length, body.Length > 500 ? body[..500] : body);
            return XDocument.Parse(StripInvalidXmlChars(body));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tally XML request failed for {Url}", url);
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

        _logger.LogInformation("Tally ledger response for {Co}: {Count} LEDGER elements",
            company.TallyCompanyName, doc.Descendants("LEDGER").Count());

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
                GSTNo          = el.Element("PARTYGSTINNO")?.Value?.Trim(),
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

        _logger.LogInformation("Tally items response for {Co}: {Count} STOCKITEM elements",
            company.TallyCompanyName, doc.Descendants("STOCKITEM").Count());

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

        _logger.LogInformation("Tally godowns response for {Co}: {Count} GODOWN elements",
            company.TallyCompanyName, doc.Descendants("GODOWN").Count());

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

    public async Task<(bool Success, string Message)> PushSaleOrderAsync(
        string tallyUrl, SaleOrder order,
        string igstLedger = "IGST", string cgstLedger = "CGST", string sgstLedger = "SGST")
    {
        var dateStr    = order.OrderDate.ToString("yyyyMMdd");
        var grandTotal = order.GrandTotal > 0 ? order.GrandTotal : order.TotalAmount;
        var itemsXml   = new StringBuilder();
        var taxXml     = new StringBuilder();

        foreach (var item in order.Items)
        {
            itemsXml.Append($@"
        <ALLINVENTORYENTRIES.LIST>
          <STOCKITEMNAME>{Escape(item.ItemName)}</STOCKITEMNAME>
          <ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE>
          <RATE>{item.Rate:F2}/{item.UOM}</RATE>
          <AMOUNT>-{item.Amount:F2}</AMOUNT>
          <ACTUALQTY>{item.Qty:F3} {item.UOM}</ACTUALQTY>
          <BILLEDQTY>{item.Qty:F3} {item.UOM}</BILLEDQTY>
          {(item.GodownName != null ? $"<BATCHALLOCATIONS.LIST><GODOWNNAME>{Escape(item.GodownName)}</GODOWNNAME><ACTUALQTY>{item.Qty:F3} {item.UOM}</ACTUALQTY><AMOUNT>-{item.Amount:F2}</AMOUNT></BATCHALLOCATIONS.LIST>" : "")}
        </ALLINVENTORYENTRIES.LIST>");
        }

        if (order.TaxType == "IGST" && order.IGSTTotal > 0)
        {
            taxXml.Append($@"
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>{Escape(igstLedger)}</LEDGERNAME>
            <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
            <AMOUNT>-{order.IGSTTotal:F2}</AMOUNT>
          </ALLLEDGERENTRIES.LIST>");
        }
        else if (order.TaxType == "CGST_SGST")
        {
            if (order.CGSTTotal > 0)
                taxXml.Append($@"
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>{Escape(cgstLedger)}</LEDGERNAME>
            <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
            <AMOUNT>-{order.CGSTTotal:F2}</AMOUNT>
          </ALLLEDGERENTRIES.LIST>");
            if (order.SGSTTotal > 0)
                taxXml.Append($@"
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>{Escape(sgstLedger)}</LEDGERNAME>
            <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
            <AMOUNT>-{order.SGSTTotal:F2}</AMOUNT>
          </ALLLEDGERENTRIES.LIST>");
        }

        var xml = $@"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><TALLYREQUEST>Import</TALLYREQUEST><TYPE>Data</TYPE><ID>Vouchers</ID></HEADER>
  <BODY><DESC/>
    <DATA>
      <TALLYMESSAGE xmlns:UDF=""TallyUDF"">
        <VOUCHER VCHTYPE=""Sales Order"" ACTION=""Create"">
          <DATE>{dateStr}</DATE>
          <NARRATION>{Escape(order.Narration ?? string.Empty)}</NARRATION>
          <VOUCHERTYPENAME>Sales Order</VOUCHERTYPENAME>
          <VOUCHERNUMBER>{Escape(order.OrderNo)}</VOUCHERNUMBER>
          <PARTYLEDGERNAME>{Escape(order.LedgerName)}</PARTYLEDGERNAME>
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>{Escape(order.LedgerName)}</LEDGERNAME>
            <ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE>
            <AMOUNT>-{grandTotal:F2}</AMOUNT>
          </ALLLEDGERENTRIES.LIST>
          {taxXml}
          {itemsXml}
        </VOUCHER>
      </TALLYMESSAGE>
    </DATA>
  </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return (false, "Could not connect to Tally");

        var lineError = doc.Descendants("LINEERROR").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(lineError)) return (false, lineError);

        if (doc.Descendants("CREATED").FirstOrDefault()?.Value == "1") return (true, "Synced");
        if (doc.Descendants("ALTERED").FirstOrDefault()?.Value == "1") return (true, "Updated");

        return (false, doc.ToString()[..Math.Min(200, doc.ToString().Length)]);
    }

    // Checks if a sales order has been converted to an invoice in Tally
    public async Task<(bool IsInvoiced, string? InvoiceNo, DateTime? InvoiceDate)> CheckInvoiceStatusAsync(
        string tallyUrl, string companyName, string orderNo)
    {
        var xml = $@"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><TALLYREQUEST>EXPORT</TALLYREQUEST><TYPE>COLLECTION</TYPE><ID>InvCheck</ID></HEADER>
  <BODY><DESC>
    <STATICVARIABLES>
      <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
      <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
    </STATICVARIABLES>
    <TDL><TDLMESSAGE>
      <COLLECTION NAME=""InvCheck"" ISINITIALIZE=""Yes"">
        <TYPE>Voucher</TYPE>
        <FILTERS>IsSales</FILTERS>
        <FETCH>VoucherNumber,Date,VoucherTypeName,ORDERLIST</FETCH>
      </COLLECTION>
      <SYSTEM:FORM NAME=""IsSales"">$$IsEqual:$VoucherTypeName:&quot;Sales&quot;</SYSTEM:FORM>
    </TDLMESSAGE></TDL>
  </DESC></BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return (false, null, null);

        foreach (var vch in doc.Descendants("VOUCHER"))
        {
            var orderLists = vch.Descendants("ORDERLIST");
            foreach (var ol in orderLists)
            {
                if (string.Equals(ol.Element("ORDERNAME")?.Value?.Trim(), orderNo, StringComparison.OrdinalIgnoreCase))
                {
                    var invNo   = vch.Element("VOUCHERNUMBER")?.Value?.Trim();
                    var dateVal = vch.Element("DATE")?.Value?.Trim();
                    DateTime? invDate = null;
                    if (dateVal?.Length == 8 &&
                        DateTime.TryParseExact(dateVal, "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var d))
                        invDate = d;
                    return (true, invNo, invDate);
                }
            }
        }

        return (false, null, null);
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

    // Get name from element: tries NAME attribute first, then NAME child element, then element own text
    private static string GetName(XElement el) =>
        el.Attribute("NAME")?.Value?.Trim()
        ?? el.Element("NAME")?.Value?.Trim()
        ?? el.Value?.Trim()
        ?? string.Empty;

    public async Task<string> GetRawXmlAsync(string tallyUrl, string companyName)
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
                <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""List of Ledger"" ISINITIALIZE=""Yes"">
                        <TYPE>Ledger</TYPE>
                        <FETCH>Name,Parent,GUID,AlterId</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";
        try
        {
            var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            var response = await _http.PostAsync(tallyUrl, content);
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // Literal invalid XML 1.0 control chars (tab=0x9, LF=0xA, CR=0xD are the only valid ones below 0x20)
    private static readonly Regex LiteralInvalidXmlChars =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    // Tally encodes control chars as XML numeric char refs: &#4; or &#x04; etc.
    // ParseNumericCharRefInline in the call stack confirms this is the actual issue.
    private static readonly Regex XmlNumericCharRef =
        new(@"&#(?:x([0-9a-fA-F]+)|([0-9]+));", RegexOptions.Compiled);

    private static string StripInvalidXmlChars(string xml)
    {
        // Step 1: remove numeric char references that resolve to invalid XML 1.0 code points
        xml = XmlNumericCharRef.Replace(xml, m =>
        {
            int cp = m.Groups[1].Success
                ? Convert.ToInt32(m.Groups[1].Value, 16)
                : int.Parse(m.Groups[2].Value);
            // Valid XML 1.0: 0x9 | 0xA | 0xD | 0x20–0xD7FF | 0xE000–0xFFFD | 0x10000+
            bool valid = cp is 0x9 or 0xA or 0xD
                      || (cp >= 0x20  && cp <= 0xD7FF)
                      || (cp >= 0xE000 && cp <= 0xFFFD)
                      || cp >= 0x10000;
            return valid ? m.Value : string.Empty;
        });

        // Step 2: strip any remaining literal control chars
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
}
