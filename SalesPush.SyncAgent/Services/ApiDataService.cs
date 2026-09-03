using System.Net.Http.Json;
using System.Text.Json;
using SalesPush.SyncAgent.Models;

namespace SalesPush.SyncAgent.Services;

// Talks to the client's Sales API over HTTPS/JSON — this agent has no local
// database at all, so its only two neighbours are the Sales API (source of
// pending invoices) and Tally (destination). Every client is expected to
// speak the same canonical contract (see docs/sales-invoice-contract.json),
// so this file only needs to know two endpoint paths plus how this client's
// API wants its auth header attached (AgentConfig.AuthHeaderName/
// AuthScheme — "Authorization: Bearer" isn't universal, e.g. Siena
// Bathroom's API wants a raw "X-Api-Key" header). Every other service
// (TallyService, SyncOrchestrator) depends only on the SaleInvoice model,
// never on how it was fetched — a client whose API doesn't match the
// contract needs a small mapping step added here, nowhere else.
public class ApiDataService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const string PendingOrdersPath = "api/tally-integration/pending-orders";
    private const string SyncResultPath    = "api/tally-integration/sync-result";

    private readonly AgentLogger _logger;

    public ApiDataService(AgentLogger logger)
    {
        _logger = logger;
    }

    private static HttpClient BuildClient(AgentConfig config)
    {
        var baseUrl = config.ApiBaseUrl;
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var apiKey = ConfigService.Unprotect(config.ApiKeyProtected);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var headerName = string.IsNullOrWhiteSpace(config.AuthHeaderName) ? "Authorization" : config.AuthHeaderName.Trim();
            var value = string.IsNullOrWhiteSpace(config.AuthScheme) ? apiKey : $"{config.AuthScheme.Trim()} {apiKey}";
            http.DefaultRequestHeaders.TryAddWithoutValidation(headerName, value);
        }
        return http;
    }

    // There's no dedicated health-check endpoint in the contract, so this
    // just confirms pending-orders is reachable with the configured auth.
    public async Task<(bool Ok, string Message)> TestConnectionAsync(AgentConfig config)
    {
        try
        {
            using var http = BuildClient(config);
            var response = await http.GetAsync(PendingOrdersPath);
            return response.IsSuccessStatusCode
                ? (true, "Connected")
                : (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // Fetches every pending sale across every company this client's API
    // knows about in one call — there's no per-company filter. Company
    // grouping/matching against what's actually open in Tally happens in
    // SyncOrchestrator, off each SaleInvoice's own CompanyName.
    public async Task<List<SaleInvoice>> GetPendingSalesAsync(AgentConfig config)
    {
        using var http = BuildClient(config);
        string raw;
        try
        {
            var response = await http.GetAsync(PendingOrdersPath);
            raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"Pending-orders request failed: HTTP {(int)response.StatusCode}. Response: {Truncate(raw)}");
                return new();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load pending sales from API", ex);
            return new();
        }

        // Parsed separately from the request itself so a non-JSON body (an
        // HTML error/login page, most often — wrong path, auth redirect,
        // reverse-proxy error) logs the actual response instead of just
        // System.Text.Json's generic "'<' is an invalid start of a value."
        try
        {
            var sales = JsonSerializer.Deserialize<List<SaleInvoice>>(raw, JsonOpts);
            return sales ?? new();
        }
        catch (JsonException jex)
        {
            _logger.Error($"Pending-orders response wasn't valid JSON ({jex.Message}). Response: {Truncate(raw)}");
            return new();
        }
    }

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "…";

    // Reports the outcome of a single Tally push back to the API so it
    // stops returning that sale from pending-orders.
    //
    // voucherNumber/voucherDate on success are best-effort: Tally's Import
    // Data response confirms CREATED but doesn't hand back the voucher
    // number it auto-assigned, so this reports our own InvoiceNo/
    // InvoiceDate for now. Revisit once a real exported voucher XML sample
    // shows whether that's good enough, or whether a follow-up Tally
    // lookup (by REMOTEID) is needed for the true assigned number.
    public async Task ReportPushResultAsync(AgentConfig config, SaleInvoice invoice, bool success, string? message)
    {
        using var http = BuildClient(config);
        try
        {
            var payload = new
            {
                saleInvoiceId = invoice.SaleInvoiceId,
                success,
                voucherNumber = success ? invoice.InvoiceNo : null,
                voucherDate = success ? invoice.InvoiceDate.ToString("yyyy-MM-dd") : null,
                message
            };
            var response = await http.PostAsJsonAsync(SyncResultPath, payload);
            if (!response.IsSuccessStatusCode)
                _logger.Warn($"API rejected sync result for SaleInvoice {invoice.SaleInvoiceId}: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to report push result for SaleInvoice {invoice.SaleInvoiceId}", ex);
        }
    }
}
