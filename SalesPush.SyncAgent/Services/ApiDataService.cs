using System.Net.Http.Headers;
using System.Net.Http.Json;
using SalesPush.SyncAgent.Models;

namespace SalesPush.SyncAgent.Services;

// Talks to the Sales API over HTTPS/JSON — this agent has no local database
// at all, so its only two neighbours are the Sales API (source of pending
// invoices) and Tally (destination). Endpoint paths and payload shapes below
// are placeholders following common REST conventions; this is the one file
// that should need to change once the real API spec is available — every
// other service (TallyService, SyncOrchestrator) only depends on the
// Company/SaleInvoice models, not on how they were fetched.
public class ApiDataService
{
    private readonly AgentLogger _logger;

    public ApiDataService(AgentLogger logger)
    {
        _logger = logger;
    }

    private static HttpClient BuildClient(string baseUrl, string apiKey)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return http;
    }

    // TODO: confirm the health-check path with the real API.
    public async Task<(bool Ok, string Message)> TestConnectionAsync(string baseUrl, string apiKey)
    {
        try
        {
            using var http = BuildClient(baseUrl, apiKey);
            var response = await http.GetAsync("api/health");
            return response.IsSuccessStatusCode
                ? (true, "Connected")
                : (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // TODO: confirm path + response shape with the real API.
    public async Task<List<Company>> GetActiveCompaniesAsync(string baseUrl, string apiKey)
    {
        using var http = BuildClient(baseUrl, apiKey);
        try
        {
            var companies = await http.GetFromJsonAsync<List<Company>>("api/companies?active=true");
            return companies ?? new();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load companies from API", ex);
            return new();
        }
    }

    // TODO: confirm path + response shape with the real API.
    public async Task<List<SaleInvoice>> GetPendingSalesAsync(string baseUrl, string apiKey, int companyId)
    {
        using var http = BuildClient(baseUrl, apiKey);
        try
        {
            var sales = await http.GetFromJsonAsync<List<SaleInvoice>>($"api/sales/pending?companyId={companyId}");
            return sales ?? new();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load pending sales from API", ex);
            return new();
        }
    }

    // TODO: confirm path + request shape with the real API.
    // Reports the outcome of a single Tally push back to the API so it can
    // mark the sale Synced/Error and stop returning it from the pending list.
    public async Task ReportPushResultAsync(
        string baseUrl, string apiKey, int saleInvoiceId, bool success, string? tallyVoucherNo, string? message)
    {
        using var http = BuildClient(baseUrl, apiKey);
        try
        {
            var payload = new
            {
                Success = success,
                TallyVoucherNo = success ? tallyVoucherNo : null,
                Message = success ? null : message
            };
            var response = await http.PostAsJsonAsync($"api/sales/{saleInvoiceId}/result", payload);
            if (!response.IsSuccessStatusCode)
                _logger.Warn($"API rejected sync result for SaleInvoice {saleInvoiceId}: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to report push result for SaleInvoice {saleInvoiceId}", ex);
        }
    }
}
