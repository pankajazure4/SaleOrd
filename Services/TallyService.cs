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
            return XDocument.Parse(DeclareUdfNamespace(StripInvalidXmlChars(body)));
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
                               OpeningBalance,ClosingBalance,CreditLimit,BillCreditPeriod,
                               LEDGSTREGDETAILS.List:GSTIN,LEDMAILINGDETAILS.List:Pincode,GUID,AlterId</FETCH>
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
        var ledgers = doc.Descendants("LEDGER")
            .Select(el => new Ledger
            {
                LedgerName     = GetName(el),
                Parent         = el.Element("PARENT")?.Value?.Trim() ?? string.Empty,
                Address        = string.Join(", ", new[] {
                                     el.Element("_ADDRESS1")?.Value,
                                     el.Element("_ADDRESS2")?.Value }
                                     .Where(v => !string.IsNullOrWhiteSpace(v))!),
                State          = el.Element("PRIORSTATENAME")?.Value?.Trim(),
                // Same story as GSTIN — Pincode lives under the nested
                // LEDMAILINGDETAILS.LIST, not as a flat field on the ledger
                // itself; confirmed against a real ledger export.
                PinCode        = el.Descendants("PINCODE").LastOrDefault()?.Value?.Trim(),
                MobileNo       = el.Element("LEDGERPHONE")?.Value?.Trim()
                              ?? el.Element("LEDGERCONTACT")?.Value?.Trim(),
                Email          = el.Element("EMAIL")?.Value?.Trim(),
                LedgerFax      = el.Element("LEDGERFAX")?.Value?.Trim(),
                // GSTIN lives under the nested LEDGSTREGDETAILS.LIST (a dated
                // list of registration records) in current Tally releases, not
                // the flat PARTYGSTIN field — confirmed against a real ledger
                // export. .LastOrDefault() picks the most recently applicable
                // registration if a ledger has more than one over time.
                GSTNo          = el.Descendants("GSTIN").LastOrDefault()?.Value?.Trim(),
                TaxType        = el.Element("TAXTYPE")?.Value?.Trim(),
                IncomeTaxNo    = el.Element("INCOMETAXNUMBER")?.Value?.Trim(),
                VATTINNo       = el.Element("VATTINNUMBER")?.Value?.Trim(),
                CreditLimit    = ParseDecimal(el.Element("CREDITLIMIT")?.Value),
                // The ledger master's own field is "BillCreditPeriod" — plain
                // "CreditPeriod" doesn't exist and silently returned nothing;
                // confirmed against a real full ledger export.
                CreditPeriod   = el.Element("BILLCREDITPERIOD")?.Value?.Trim(),
                OpeningBalance = ParseDecimal(el.Element("OPENINGBALANCE")?.Value),
                ClosingBalance = ParseDecimal(el.Element("CLOSINGBALANCE")?.Value),
                GUID           = el.Element("GUID")?.Value?.Trim(),
                AlterId        = ParseLong(el.Element("ALTERID")?.Value),
                CompanyId      = company.CompanyId,
                LastSyncedAt   = now
            })
            .Where(l => !string.IsNullOrWhiteSpace(l.LedgerName))
            .ToList();

        // A ledger with no CreditPeriod of its own displays its parent
        // Group's default in Tally's UI (confirmed on a real ledger export —
        // "Sundry Debtors" showing "1 Days" for a ledger whose own record has
        // no CreditPeriod at all), but that resolution only happens client-side
        // when you pick the party interactively — it's not in the XML export,
        // and an XML-imported voucher won't get it either unless we resolve it
        // ourselves. Only bother calling Tally for Group data if some ledger
        // actually needs it.
        if (ledgers.Any(l => string.IsNullOrWhiteSpace(l.CreditPeriod)))
        {
            var groupCreditPeriods = await GetGroupCreditPeriodsAsync(tallyUrl, company.TallyCompanyName);
            foreach (var l in ledgers.Where(l => string.IsNullOrWhiteSpace(l.CreditPeriod)))
                l.CreditPeriod = ResolveGroupCreditPeriod(l.Parent, groupCreditPeriods);
        }

        return ledgers;
    }

    // Fetches every Group's own Parent/CreditPeriod so blank ledger-level
    // CreditPeriod can be resolved by walking up the group hierarchy — see
    // the call site in GetLedgersAsync.
    private async Task<Dictionary<string, (string? Parent, string? CreditPeriod)>> GetGroupCreditPeriodsAsync(
        string tallyUrl, string companyName)
    {
        var xml = $@"<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Groups</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""List of Groups"" ISINITIALIZE=""Yes"">
                        <TYPE>Group</TYPE>
                        <FETCH>Name,Parent,BillCreditPeriod</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        return doc.Descendants("GROUP")
            .GroupBy(el => GetName(el), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (g.Last().Element("PARENT")?.Value?.Trim(), g.Last().Element("BILLCREDITPERIOD")?.Value?.Trim()),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveGroupCreditPeriod(string? groupName, Dictionary<string, (string? Parent, string? CreditPeriod)> groups)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = groupName;
        while (!string.IsNullOrWhiteSpace(current) && visited.Add(current))
        {
            if (!groups.TryGetValue(current, out var g)) return null;
            if (!string.IsNullOrWhiteSpace(g.CreditPeriod)) return g.CreditPeriod;
            current = g.Parent;
        }
        return null;
    }

    // A stock item's GST% is date-slabbed in Tally (rate notifications change
    // it over time) — GSTDETAILS.List carries the full dated history, unlike
    // the single-value RateOfDuty field. Keyed by item name here since these
    // records exist before the item's own StockItemId is assigned; the
    // caller resolves that during upsert.
    public class StockItemTaxSlabRecord
    {
        public string ItemName { get; set; } = "";
        public DateTime ApplicableFrom { get; set; }
        public decimal CGSTRate { get; set; }
        public decimal SGSTRate { get; set; }
        public decimal IGSTRate { get; set; }
        public decimal CessRate { get; set; }
        public decimal StateCessRate { get; set; }
    }

    public async Task<(List<StockItem> Items, List<StockItemTaxSlabRecord> TaxSlabs)> GetStockItemsAsync(
        string tallyUrl, Company company)
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
                               OpeningValue,ClosingValue,IsBatchwiseOn,IsCostTrackingOn,
                               GSTDetails</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return (new(), new());

        _logger.LogInformation("Tally items response for {Co}: {Count} STOCKITEM elements",
            company.TallyCompanyName, doc.Descendants("STOCKITEM").Count());

        var now = DateTime.Now;
        var items = new List<StockItem>();
        var taxSlabs = new List<StockItemTaxSlabRecord>();

        foreach (var el in doc.Descendants("STOCKITEM"))
        {
            var itemName = GetName(el);
            if (string.IsNullOrWhiteSpace(itemName)) continue;

            items.Add(new StockItem
            {
                ItemName        = itemName,
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
            });

            foreach (var gst in el.Elements("GSTDETAILS.LIST"))
            {
                var dateVal = gst.Element("APPLICABLEFROM")?.Value?.Trim();
                if (dateVal?.Length != 8 || !DateTime.TryParseExact(dateVal, "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var applicableFrom))
                    continue;

                // Only the "Any" state-wise block is used — per-state GST
                // overrides exist in Tally but aren't a case this app needs.
                var stateWise = gst.Elements("STATEWISEDETAILS.LIST").FirstOrDefault();
                if (stateWise == null) continue;

                decimal RateFor(string dutyHead) => ParseDecimal(
                    stateWise.Elements("RATEDETAILS.LIST")
                        .FirstOrDefault(r => string.Equals(r.Element("GSTRATEDUTYHEAD")?.Value?.Trim(), dutyHead, StringComparison.OrdinalIgnoreCase))
                        ?.Element("GSTRATE")?.Value);

                taxSlabs.Add(new StockItemTaxSlabRecord
                {
                    ItemName       = itemName,
                    ApplicableFrom = applicableFrom,
                    CGSTRate       = RateFor("CGST"),
                    SGSTRate       = RateFor("SGST/UTGST"),
                    IGSTRate       = RateFor("IGST"),
                    CessRate       = RateFor("Cess"),
                    StateCessRate  = RateFor("State Cess")
                });
            }
        }

        return (items, taxSlabs);
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
        string tallyUrl, SaleOrder order, string companyName,
        string salesLedger  = "Sales",
        string igstLedger   = "IGST",
        string cgstLedger   = "CGST",
        string sgstLedger   = "SGST",
        string roundOffLedger = "Round Off",
        bool   isAlter      = false,
        string voucherType  = "Sales Order")
    {
        var action     = isAlter ? "Alter" : "Create";
        var dateStr    = order.OrderDate.ToString("yyyyMMdd");
        var grandTotal = order.GrandTotal > 0 ? order.GrandTotal : order.TotalAmount;
        var narration  = string.IsNullOrWhiteSpace(order.Narration)
            ? $"Ref: {order.OrderNo}"
            : $"Ref: {order.OrderNo} | {order.Narration}";
        var remoteId   = $"saleord{order.SaleOrderId:D7}";
        var dueDate    = order.DeliveryDate ?? order.OrderDate;

        // ── Inventory entries ────────────────────────────────────────────────
        var itemsXml = new StringBuilder();
        foreach (var item in order.Items)
        {
            var rateStr = $"{item.Rate:F2}/{item.UOM}";
            var qtyStr  = $" {item.Qty:F3} {item.UOM}";

            // ORDERNO/ORDERDUEDATE live inside BATCHALLOCATIONS.LIST in
            // Tally's schema, but they're order-tracking fields a Sales
            // Order voucher needs on every item regardless of whether that
            // item actually has a godown/batch assigned — confirmed against
            // a real Tally export: BATCHNAME is present even for a
            // non-batch-tracked item. Previously this whole block (so
            // ORDERNO and ORDERDUEDATE too) was skipped entirely whenever
            // GodownName was empty, which is exactly what produced "Due
            // date of order is missing in item allocations" on push — Tally
            // requires ORDERDUEDATE per item allocation on this voucher
            // type unconditionally. GODOWNNAME is the only piece that's
            // genuinely conditional here.
            //
            // BATCHNAME's real export value for "no batch picked" is the raw
            // control character 0x04 followed by "Any" — Tally itself writes
            // this as the character reference "&#4;" since 0x04 can't appear
            // as a literal byte in XML text. That "&#4;" prefix IS the thing
            // that marks it as Tally's reserved "Any" sentinel rather than a
            // real batch name — plain "Any" with no prefix was tried first
            // and Tally read it as a literal (nonexistent) batch name,
            // auto-creating a new one on every single push (duplicate batch
            // masters). "&#4; Any" below is written as a raw literal, NOT
            // run through Escape() — Escape() would turn "&" into "&amp;"
            // and the whole point (Tally's XML parser decoding "&#4;" back
            // into the raw 0x04 byte) would be lost.
            var jd = TallyJd(dueDate);
            var p  = dueDate.ToString("d-MMM-yy");
            var godownXml = string.IsNullOrWhiteSpace(item.GodownName)
                ? ""
                : $"<GODOWNNAME>{Escape(item.GodownName)}</GODOWNNAME>";
            var batchXml = $@"
              <BATCHALLOCATIONS.LIST>
                {godownXml}
                <BATCHNAME>&#4; Any</BATCHNAME>
                <ORDERNO>{Escape(order.OrderNo)}</ORDERNO>
                <AMOUNT>{item.Amount:F2}</AMOUNT>
                <ACTUALQTY>{qtyStr}</ACTUALQTY>
                <BILLEDQTY>{qtyStr}</BILLEDQTY>
                <ORDERDUEDATE JD=""{jd}"" P=""{p}"">{p}</ORDERDUEDATE>
                <ADDITIONALDETAILS.LIST></ADDITIONALDETAILS.LIST>
                <VOUCHERCOMPONENTLIST.LIST></VOUCHERCOMPONENTLIST.LIST>
              </BATCHALLOCATIONS.LIST>";

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

        // ── Ledger entries ────────────────────────────────────────────────────
        var ledgersXml = new StringBuilder();

        // Party ledger — positive (owed by customer), so amount is negative in Tally DR/CR convention
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

        // Tax ledgers
        if (order.TaxType == "IGST" && order.IGSTTotal > 0)
        {
            ledgersXml.Append(TaxLedgerEntry(igstLedger, order.IGSTTotal));
        }
        else if (order.TaxType == "CGST_SGST")
        {
            if (order.CGSTTotal > 0) ledgersXml.Append(TaxLedgerEntry(cgstLedger, order.CGSTTotal));
            if (order.SGSTTotal > 0) ledgersXml.Append(TaxLedgerEntry(sgstLedger, order.SGSTTotal));
        }

        // Round Off
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

        // ── Party (buyer) GST / address details ─────────────────────────────
        // Tally auto-fills these when a party is picked manually in its UI, but
        // an XML import gets none of that for free — they must be sent explicitly
        // or the voucher lands in Tally with a blank buyer address/GSTIN.
        var partyGstin  = order.Ledger?.GSTNo?.Trim() ?? "";
        var partyState  = order.Ledger?.State?.Trim() ?? "";
        var partyPincode = order.Ledger?.PinCode?.Trim() ?? "";
        var partyCreditPeriod = order.Ledger?.CreditPeriod?.Trim() ?? "";
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
            {(string.IsNullOrEmpty(partyPincode) ? "" : $"<PARTYPINCODE>{Escape(partyPincode)}</PARTYPINCODE>")}
            <GSTREGISTRATIONTYPE>{gstRegistrationType}</GSTREGISTRATIONTYPE>
            <CONSIGNEECOUNTRYNAME>India</CONSIGNEECOUNTRYNAME>
            {(string.IsNullOrEmpty(partyGstin) ? "" : $"<CONSIGNEEGSTIN>{Escape(partyGstin)}</CONSIGNEEGSTIN>")}
            {(string.IsNullOrEmpty(partyState) ? "" : $"<CONSIGNEESTATENAME>{Escape(partyState)}</CONSIGNEESTATENAME>")}
            {(string.IsNullOrEmpty(partyPincode) ? "" : $"<CONSIGNEEPINCODE>{Escape(partyPincode)}</CONSIGNEEPINCODE>")}
            {(string.IsNullOrEmpty(partyCreditPeriod) ? "" : $"<TERMSOFPAYMENT>{Escape(partyCreditPeriod)}</TERMSOFPAYMENT>")}
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

    // Creates a new party ledger master in Tally (Masters > Party Master feature).
    // GST/address live under LEDGSTREGDETAILS.LIST / LEDMAILINGDETAILS.LIST in
    // current Tally releases (confirmed against a real ledger export) — the
    // flat STATENAME/PARTYGSTIN/LEDGERPHONE tags used by older schemas are
    // silently ignored here, which is why an earlier version of this method
    // saved the party locally but the GST/address never actually landed in Tally.
    //
    // FSSAINo has no standard Tally field — some installs expose it via a
    // custom TDL "User Defined Field" (e.g. UDF:_UDF_788534681), but that
    // field's internal ID is assigned per-install and isn't portable across
    // Tally companies. fssaiUdfField is admin-configured (Settings > Order
    // Defaults) specifically so this stays a no-op — FSSAI simply isn't sent —
    // on any install that hasn't defined a matching UDF, instead of guessing
    // and risking a rejected import or a value landing in the wrong field.
    public async Task<(bool Success, string Message)> PushLedgerAsync(
        string tallyUrl, Ledger ledger, string companyName, string? fssaiUdfField = null)
    {
        var today = DateTime.Today.ToString("yyyyMMdd");
        var hasGstin = !string.IsNullOrWhiteSpace(ledger.GSTNo);
        var hasState = !string.IsNullOrWhiteSpace(ledger.State);

        var udfFieldName = fssaiUdfField?.Trim().Replace("UDF:", "", StringComparison.OrdinalIgnoreCase).Trim();
        var udfXml = string.IsNullOrWhiteSpace(udfFieldName) || string.IsNullOrWhiteSpace(ledger.FSSAINo)
            ? ""
            : $@"
            <UDF:{udfFieldName}.LIST DESC="""" ISLIST=""YES"" TYPE=""String"">
              <UDF:{udfFieldName} DESC="""">{Escape(ledger.FSSAINo)}</UDF:{udfFieldName}>
            </UDF:{udfFieldName}.LIST>";

        var addressLines = (ledger.Address ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var addressXml = addressLines.Length == 0 ? "" : $@"
              <ADDRESS.LIST TYPE=""String"">{string.Concat(addressLines.Select(a => $@"
                <ADDRESS>{Escape(a)}</ADDRESS>"))}
              </ADDRESS.LIST>";

        var gstRegDetailsXml = $@"
            <LEDGSTREGDETAILS.LIST>
              <APPLICABLEFROM>{today}</APPLICABLEFROM>
              <GSTREGISTRATIONTYPE>{(hasGstin ? "Regular" : "Unregistered")}</GSTREGISTRATIONTYPE>
              {(hasState ? $"<STATE>{Escape(ledger.State!)}</STATE>" : "")}
              {(hasState ? $"<PLACEOFSUPPLY>{Escape(ledger.State!)}</PLACEOFSUPPLY>" : "")}
              {(hasGstin ? $"<GSTIN>{Escape(ledger.GSTNo!)}</GSTIN>" : "")}
            </LEDGSTREGDETAILS.LIST>";

        var mailingDetailsXml = $@"
            <LEDMAILINGDETAILS.LIST>{addressXml}
              <APPLICABLEFROM>{today}</APPLICABLEFROM>
              <MAILINGNAME>{Escape(ledger.LedgerName)}</MAILINGNAME>
              {(hasState ? $"<STATE>{Escape(ledger.State!)}</STATE>" : "")}
              <COUNTRY>India</COUNTRY>
            </LEDMAILINGDETAILS.LIST>";

        var xml = $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Import Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <IMPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>All Masters</REPORTNAME>
        <STATICVARIABLES>
          <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
        </STATICVARIABLES>
      </REQUESTDESC>
      <REQUESTDATA>
        <TALLYMESSAGE xmlns:UDF=""TallyUDF"">
          <LEDGER NAME=""{Escape(ledger.LedgerName)}"" ACTION=""Create"">
            <NAME>{Escape(ledger.LedgerName)}</NAME>
            <PARENT>Sundry Debtors</PARENT>
            <ISBILLWISEON>Yes</ISBILLWISEON>
            <COUNTRYOFRESIDENCE>India</COUNTRYOFRESIDENCE>
            {(string.IsNullOrWhiteSpace(ledger.MobileNo) ? "" : $"<LEDGERMOBILE>{Escape(ledger.MobileNo)}</LEDGERMOBILE>")}
            {/* BILLCREDITPERIOD, not CreditPeriod — same field name GetLedgersAsync
               already confirmed against a real ledger export (see its comment). */
             (string.IsNullOrWhiteSpace(ledger.CreditPeriod) ? "" : $"<BILLCREDITPERIOD>{Escape(ledger.CreditPeriod)}</BILLCREDITPERIOD>")}{gstRegDetailsXml}{mailingDetailsXml}{udfXml}
          </LEDGER>
        </TALLYMESSAGE>
      </REQUESTDATA>
    </IMPORTDATA>
  </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return (false, "Could not connect to Tally");

        var lineError = doc.Descendants("LINEERROR").FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(lineError)) return (false, lineError);

        if (doc.Descendants("CREATED").FirstOrDefault()?.Value == "1") return (true, "Created in Tally");
        if (doc.Descendants("ALTERED").FirstOrDefault()?.Value == "1") return (true, "Updated in Tally");

        var raw = doc.ToString();
        return (false, raw[..Math.Min(300, raw.Length)]);
    }

    public class InvoiceLookupRecord
    {
        public string? InvoiceNo { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public List<string> OrderRefs { get; set; } = new();
    }

    // Fetches Sales-type vouchers in [fromDate, today] ONCE, so a whole batch
    // of pending orders can be matched against a single Tally round-trip.
    //
    // salesVoucherTypeNames must be the FULL list of voucher types that
    // behave as a sale (Abbreviation="Sale") — see GetSalesVoucherTypeNamesAsync
    // below, the same discovery the voucher-inventory sync already uses.
    // This used to hardcode a single "$VoucherTypeName:\"Sales\"" filter,
    // which only matches a voucher type literally named "Sales" — on this
    // client's install there are several Sales-abbreviation types with other
    // names (Sales-2, Sales 23-24 New Latest, Sales New 23-24 -Dont Use —
    // see the "Sales voucher types matched" log line from master sync), so
    // any invoice raised under one of those never matched here and its
    // order's IsInvoiced flag silently never got set, even though the
    // invoice genuinely existed in Tally.
    //
    // This replaces the old per-order CheckInvoiceStatusAsync, which queried
    // Tally's *entire* Sales voucher history (no date bound at all) once for
    // EVERY uninvoiced order — up to 50 full-history scans every
    // OrderPushIntervalMinutes. On a client install with several years of
    // sales history, that was enough to crash Tally's native HTTP engine
    // outright (STATUS_ACCESS_VIOLATION / c0000005) — confirmed by the error
    // stopping the moment the IIS site (and so this background job) was
    // stopped. Bounding by date and fetching once per company per cycle
    // fixes both the crash and the wasted repeated full-history scans.
    //
    // No EXPLODEVCHTYPE: comparing against a reference project (LoheBgService)
    // that reliably imports/reads 200-300 Tally records per run without ever
    // crashing Tally — its only EXPORT query is a single exact-name-filtered
    // ledger lookup, never a bulk voucher scan with that flag set. REFERENCE
    // and INVOICEORDERLIST.LIST are both generic Voucher fields present on
    // every voucher type regardless of class, so forcing full type-hierarchy
    // resolution for each one was unnecessary overhead, not a requirement.
    public async Task<List<InvoiceLookupRecord>> GetRecentSalesInvoicesAsync(
        string tallyUrl, string companyName, DateTime fromDate, List<string> salesVoucherTypeNames)
    {
        if (salesVoucherTypeNames.Count == 0) return new();

        // Single Escape() call for the whole formula, not per-name — escaping
        // each name individually double-encodes the quotes into literal
        // "&quot;" text that Tally's formula parser rejects outright. Same
        // proven pattern as GetVoucherInventoryAsync's typeMatch.
        var typeMatch = string.Join(" OR ", salesVoucherTypeNames.Select(n =>
            $@"$$IsEqual:$VoucherTypeName:""{n}"""));

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
      <SYSTEM TYPE=""Formulae"" NAME=""IsSales"">{Escape(typeMatch)}</SYSTEM>
    </TDLMESSAGE></TDL>
  </DESC></BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        var results = new List<InvoiceLookupRecord>();
        foreach (var vch in doc.Descendants("VOUCHER"))
        {
            // Two independent places carry the originating SO number on a real
            // Tally invoice: the flat REFERENCE field (Tally auto-fills this with
            // the order number when the invoice is raised "against" an order —
            // confirmed on a real exported invoice), and the structured
            // INVOICEORDERLIST.LIST > BASICPURCHASEORDERNO. REFERENCE is a plain
            // scalar so it fetches reliably; the nested list has proven unreliable
            // to populate via a narrow COLLECTION FETCH, so it's kept only as a
            // fallback in case REFERENCE isn't set on some invoices.
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

    // Finds every voucher type that behaves as a sale — not just the one
    // literally named "Sales". A company can rename it or add extra
    // sale-derived types (e.g. "Sales- Stock"), and matching only the exact
    // name "$VoucherTypeName = \"Sales\"" silently misses those. Abbreviation
    // is Tally's own semantic marker for this ("Sale" regardless of display
    // name), so this is queried once per sync cycle and the resulting name
    // list is used to build the voucher FILTER dynamically.
    public async Task<List<string>> GetSalesVoucherTypeNamesAsync(string tallyUrl, string companyName)
    {
        var xml = $@"<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Sales Voucher Types</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""List of Sales Voucher Types"" ISINITIALIZE=""Yes"">
                        <TYPE>Voucher Type</TYPE>
                        <FETCH>Name,Parent,MailingName</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return new();

        var names = doc.Descendants("VOUCHERTYPE")
            .Where(el => string.Equals(el.Element("MAILINGNAME")?.Value?.Trim(), "Sale", StringComparison.OrdinalIgnoreCase))
            .Select(el => GetName(el))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Fall back to the literal name if MailingName lookup found nothing
        // (e.g. this Tally install exposes it differently) — better to sync
        // the common case than sync nothing at all.
        return names.Count > 0 ? names : new List<string> { "Sales" };
    }

    // Tally's own record of when this company's books begin — the starting
    // point for the one-time full historical voucher-inventory batch sync.
    public async Task<DateTime?> GetCompanyStartDateAsync(string tallyUrl, string companyName)
    {
        var xml = $@"<ENVELOPE>
    <HEADER>
        <VERSION>1</VERSION>
        <TALLYREQUEST>EXPORT</TALLYREQUEST>
        <TYPE>COLLECTION</TYPE>
        <ID>List of Companies</ID>
    </HEADER>
    <BODY>
        <DESC>
            <STATICVARIABLES>
                <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
            </STATICVARIABLES>
            <TDL>
                <TDLMESSAGE>
                    <COLLECTION NAME=""List of Companies"" ISINITIALIZE=""Yes"">
                        <TYPE>Company</TYPE>
                        <FETCH>Name,StartingFrom</FETCH>
                    </COLLECTION>
                </TDLMESSAGE>
            </TDL>
        </DESC>
    </BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return null;

        var raw = doc.Descendants("COMPANY").FirstOrDefault()?.Element("STARTINGFROM")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (DateTime.TryParseExact(raw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d))
            return d;
        return DateTime.TryParse(raw, out var d2) ? d2 : null;
    }

    public class VoucherInventoryRecord
    {
        public string GUID { get; set; } = "";
        public string VoucherNumber { get; set; } = "";
        public string VoucherTypeName { get; set; } = "";
        public DateTime VoucherDate { get; set; }
        public long AlterId { get; set; }
        public string PartyLedgerName { get; set; } = "";
        public string StockItemName { get; set; } = "";
        public decimal ActualQty { get; set; }
        public decimal BilledQty { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public decimal Discount { get; set; }
        public string? GodownName { get; set; }
    }

    // Line-level inventory data for every Sales-type voucher (any voucher
    // type whose Abbreviation is "Sale" — see GetSalesVoucherTypeNamesAsync).
    // Two mutually exclusive modes, matching the "full history once, then
    // incremental" pattern proven in the SyncMast project:
    //   - Date-range mode (fromDate/toDate set, sinceAlterId null): used for
    //     the one-time historical backfill, called once per date chunk.
    //   - Incremental mode (sinceAlterId set, fromDate/toDate null): used for
    //     every sync after the backfill completes — only vouchers Tally has
    //     created/altered since the last sync, regardless of date, via
    //     $AlterId > sinceAlterId. No repeated full-history rescanning.
    // Success=false means Tally couldn't be reached at all for this batch
    // (distinct from a successful, empty result) — callers must NOT treat
    // that as "this date range/watermark has no data" and advance past it,
    // or a transient failure silently truncates the historical backfill.
    public async Task<(List<VoucherInventoryRecord> Records, bool Success)> GetVoucherInventoryAsync(
        string tallyUrl, string companyName, List<string> salesVoucherTypeNames,
        DateTime? fromDate, DateTime? toDate, long? sinceAlterId)
    {
        if (salesVoucherTypeNames.Count == 0) return (new(), true);

        // NOTE: only ONE Escape() call for the whole formula (below, at the
        // <SYSTEM> element) — escaping the quoted name here too double-
        // encoded the quotes into literal "&quot;" text that Tally's formula
        // parser then rejected outright ("Bad formula!"), confirmed via a
        // live Postman test against the client's Tally.
        var typeMatch = string.Join(" OR ", salesVoucherTypeNames.Select(n =>
            $@"$$IsEqual:$VoucherTypeName:""{n}"""));
        var filterFormula = sinceAlterId.HasValue
            ? $"({typeMatch}) AND $AlterId > {sinceAlterId.Value}"
            : typeMatch;

        var dateVars = (fromDate.HasValue && toDate.HasValue)
            ? $@"
      <SVFROMDATE TYPE=""Date"">{fromDate.Value:yyyyMMdd}</SVFROMDATE>
      <SVTODATE TYPE=""Date"">{toDate.Value:yyyyMMdd}</SVTODATE>"
            : "";

        var xml = $@"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><TALLYREQUEST>EXPORT</TALLYREQUEST><TYPE>COLLECTION</TYPE><ID>VoucherInventory</ID></HEADER>
  <BODY><DESC>
    <STATICVARIABLES>
      <SVCURRENTCOMPANY>{Escape(companyName)}</SVCURRENTCOMPANY>{dateVars}
      <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
    </STATICVARIABLES>
    <TDL><TDLMESSAGE>
      <COLLECTION NAME=""VoucherInventory"" ISINITIALIZE=""Yes"">
        <TYPE>Voucher</TYPE>
        <FILTER>IsMatchingSalesVoucher</FILTER>
        <FETCH>GUID,VoucherNumber,VoucherTypeName,Date,PartyLedgerName,AlterId,
               AllInventoryEntries.List:StockItemName,
               AllInventoryEntries.List:ActualQty,
               AllInventoryEntries.List:BilledQty,
               AllInventoryEntries.List:Rate,
               AllInventoryEntries.List:Amount,
               AllInventoryEntries.List:Discount,
               AllInventoryEntries.List:BatchAllocations.List:GodownName</FETCH>
      </COLLECTION>
      <SYSTEM TYPE=""Formulae"" NAME=""IsMatchingSalesVoucher"">{Escape(filterFormula)}</SYSTEM>
    </TDLMESSAGE></TDL>
  </DESC></BODY>
</ENVELOPE>";

        var doc = await PostXmlAsync(tallyUrl, xml);
        if (doc == null) return (new(), false);

        var results = new List<VoucherInventoryRecord>();
        foreach (var vch in doc.Descendants("VOUCHER"))
        {
            var guid = vch.Element("GUID")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(guid)) continue;

            var dateVal = vch.Element("DATE")?.Value?.Trim();
            if (dateVal?.Length != 8 || !DateTime.TryParseExact(dateVal, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var voucherDate))
                continue;

            var voucherNumber = vch.Element("VOUCHERNUMBER")?.Value?.Trim() ?? "";
            var voucherTypeName = vch.Element("VOUCHERTYPENAME")?.Value?.Trim() ?? "";
            var partyLedgerName = vch.Element("PARTYLEDGERNAME")?.Value?.Trim() ?? "";
            var alterId = ParseLong(vch.Element("ALTERID")?.Value);

            foreach (var entry in vch.Descendants("ALLINVENTORYENTRIES.LIST"))
            {
                var itemName = entry.Element("STOCKITEMNAME")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(itemName)) continue;

                results.Add(new VoucherInventoryRecord
                {
                    GUID = guid,
                    VoucherNumber = voucherNumber,
                    VoucherTypeName = voucherTypeName,
                    VoucherDate = voucherDate,
                    AlterId = alterId,
                    PartyLedgerName = partyLedgerName,
                    StockItemName = itemName,
                    ActualQty = ParseQty(entry.Element("ACTUALQTY")?.Value),
                    BilledQty = ParseQty(entry.Element("BILLEDQTY")?.Value),
                    Rate = ParseRate(entry.Element("RATE")?.Value),
                    Amount = ParseDecimal(entry.Element("AMOUNT")?.Value),
                    Discount = ParseDecimal(entry.Element("DISCOUNT")?.Value),
                    GodownName = entry.Descendants("BATCHALLOCATIONS.LIST")
                        .FirstOrDefault()?.Element("GODOWNNAME")?.Value?.Trim()
                });
            }
        }
        return (results, true);
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

    // Vouchers on a Voucher Type with User Defined Fields configured get their
    // UDF values wrapped in <UDF:...> tags in Tally's XML export, but Tally
    // never declares the "UDF" namespace prefix on the root <ENVELOPE> —
    // XDocument.Parse then throws "'UDF' is an undeclared prefix" for any
    // batch that happens to include such a voucher. We don't read UDF data at
    // all, so it's enough to just declare the prefix so the doc parses.
    private static string DeclareUdfNamespace(string xml) =>
        xml.Contains("UDF:") && !xml.Contains("xmlns:UDF")
            ? xml.Replace("<ENVELOPE>", "<ENVELOPE xmlns:UDF=\"TallyUDF\">")
            : xml;

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

    // Tally exports voucher line RATE as "100.00/Nos" — strip the unit suffix.
    private static decimal ParseRate(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0;
        var slashIdx = val.IndexOf('/');
        if (slashIdx > 0) val = val[..slashIdx];
        return ParseDecimal(val);
    }

    // Tally exports quantity as "2 Pcs" / "-2.5 Kg" — keep the leading
    // numeric part (with optional sign/decimal), drop the trailing unit text.
    private static readonly Regex LeadingNumber = new(@"^\s*(-?[\d,]+(?:\.\d+)?)", RegexOptions.Compiled);

    private static decimal ParseQty(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0;
        var m = LeadingNumber.Match(val);
        return m.Success ? ParseDecimal(m.Groups[1].Value) : 0;
    }
}
