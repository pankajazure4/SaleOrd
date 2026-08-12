using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaleOrd.SyncAgent.Licensing;

// Talks to the same LicenseActivator portal SaleOrd, SyncMast and
// SalesPush.SyncAgent already use — this product is registered there under
// its own ProductCode ("SALEORDAGENT"), separate from the SaleOrd web app's
// own "SALEORD" license, so activating one doesn't consume or interact with
// the other's MaxActivations count. Throws on network failure so callers can
// distinguish "portal unreachable" (fall back to the offline grace period)
// from "portal said no" (hard fail).
public static class LicensePortalClient
{
    // Vendor-controlled endpoint — deliberately a hardcoded constant, never
    // read from config.json or shown/editable in any customer-facing UI.
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

        // Worst case (both attempts needed) used to be 15s+30s = 45s of a
        // frozen, unresponsive window on a slow/unreachable portal. Trimmed
        // to 6s+10s — still generous for a real network, far less punishing
        // when it's not reachable at all (mirrored from SaleOrd web app).
        string responseBody;
        try
        {
            responseBody = await PostAsync(url, json, useProxy: true, seconds: 6);
        }
        catch
        {
            responseBody = await PostAsync(url, json, useProxy: false, seconds: 10);
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
            IsTrial = parsed.IsTrial
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

    // Only the fields this agent cares about are mapped; the portal's other
    // fields (MaxUsers, MaxCompanies, report-designer flags, etc.) are simply
    // ignored by System.Text.Json — this is a single-machine desktop agent,
    // not a multi-user product.
    private class PortalValidateResponse
    {
        public bool Valid { get; set; }
        public string? Message { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public bool IsTrial { get; set; }
    }
}
