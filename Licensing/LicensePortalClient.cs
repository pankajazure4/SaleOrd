using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SaleOrd.Licensing;

// Talks to the LicenseActivator portal's /api/license/validate endpoint.
// Throws on network failure so callers can distinguish "portal unreachable"
// (fall back to offline grace period) from "portal said no" (hard fail).
public static class LicensePortalClient
{
    // Vendor-controlled endpoint — deliberately a hardcoded constant, never
    // read from appsettings.json or shown/editable in any customer-facing UI.
    // Same LicenseActivator instance SyncMast already talks to.
    private const string PORTAL_BASE_URL = "https://103.105.22.180:7002";

    // The server runs on a bare IP with a self-signed cert (no public domain
    // yet). Set this to false once a real SSL cert is installed there.
    private const bool TRUST_ANY_SERVER_CERT = true;

    public static async Task<LicenseValidationResult> ValidateAsync(
        string productCode,
        string licenseKey,
        string machineFingerprint,
        string machineLabel)
    {
        var url = PORTAL_BASE_URL.TrimEnd('/') + "/api/license/validate";

        var payload = new PortalValidateRequest
        {
            ProductCode = productCode,
            LicenseKey = licenseKey,
            MachineFingerprint = machineFingerprint,
            MachineLabel = machineLabel
        };
        var json = JsonSerializer.Serialize(payload);

        string responseBody;
        try
        {
            responseBody = await PostAsync(url, json, useProxy: true, seconds: 15);
        }
        catch
        {
            responseBody = await PostAsync(url, json, useProxy: false, seconds: 30);
        }

        var parsed = JsonSerializer.Deserialize<PortalValidateResponse>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed == null)
            return new LicenseValidationResult { IsValid = false, Message = "Empty response from license server." };

        return new LicenseValidationResult
        {
            IsValid = parsed.Valid,
            Message = parsed.Message ?? string.Empty,
            ExpiresOn = parsed.ExpiresOn,
            IsTrial = parsed.IsTrial,
            MaxUsers = parsed.MaxUsers,
            MaxCompanies = parsed.MaxCompanies
        };
    }

    // Resilient POST for "unknown" client networks: system proxy first
    // (corporate machines) with a short timeout, then a direct connection.
    private static async Task<string> PostAsync(string url, string json, bool useProxy, int seconds)
    {
        using var handler = new HttpClientHandler { UseProxy = useProxy };
        if (TRUST_ANY_SERVER_CERT)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(seconds) };
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content);
        return await response.Content.ReadAsStringAsync();
    }

    private class PortalValidateRequest
    {
        [JsonPropertyName("productCode")] public string ProductCode { get; set; } = "";
        [JsonPropertyName("licenseKey")] public string LicenseKey { get; set; } = "";
        [JsonPropertyName("machineFingerprint")] public string MachineFingerprint { get; set; } = "";
        [JsonPropertyName("machineLabel")] public string MachineLabel { get; set; } = "";
    }

    // Only the fields SaleOrd cares about are mapped; the portal's
    // report-designer-only fields (MaxCustomReports, CanCreateReports, etc.)
    // are simply ignored by System.Text.Json.
    private class PortalValidateResponse
    {
        public bool Valid { get; set; }
        public string? Message { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public bool IsTrial { get; set; }
        public int? MaxUsers { get; set; }
        public int? MaxCompanies { get; set; }
    }
}
