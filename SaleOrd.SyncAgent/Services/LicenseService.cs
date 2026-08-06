using System.Globalization;
using SaleOrd.SyncAgent.Licensing;
using SaleOrd.SyncAgent.Models;

namespace SaleOrd.SyncAgent.Services;

// Single entry point for the startup license gate (Program.cs) and the
// "License" section on FrmMain's Configuration tab. Ported from
// SalesPush.SyncAgent's LicenseService — same design, just under its own
// ProductCode ("SALEORDAGENT", separate from the SaleOrd web app's own
// "SALEORD" license) and reusing this project's existing
// ConfigService.ProtectPassword/UnprotectPassword instead of adding a
// second, differently-named DPAPI wrapper.
public class LicenseService
{
    private const string PRODUCT_CODE = "SALEORDAGENT";
    private const int MAX_OFFLINE_DAYS = 7;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);

    private readonly ConfigService _configSvc;
    private LicenseValidationResult? _cached;
    private DateTime _cachedAtUtc;

    public LicenseService(ConfigService configSvc)
    {
        _configSvc = configSvc;
    }

    public static string GetMachineId() => MachineFingerprint.GetUniqueMachineId();

    public static string GetLicenseKey(AgentConfig config) => ConfigService.UnprotectPassword(config.LicenseKeyProtected);

    public void SaveLicenseKey(AgentConfig config, string plainKey)
    {
        config.LicenseKeyProtected = ConfigService.ProtectPassword(plainKey.Trim());
        _configSvc.Save(config);
        Invalidate();
    }

    public void Invalidate() => _cached = null;

    public async Task<LicenseValidationResult> CheckAsync(AgentConfig config)
    {
        if (_cached != null && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
            return _cached;

        var result = await CheckUncachedAsync(config);
        _cached = result;
        _cachedAtUtc = DateTime.UtcNow;
        return result;
    }

    private async Task<LicenseValidationResult> CheckUncachedAsync(AgentConfig config)
    {
        var machineId = GetMachineId();
        var licenseKey = GetLicenseKey(config);

        if (string.IsNullOrWhiteSpace(licenseKey))
            return Fail("License not activated.");

        try
        {
            var result = await LicensePortalClient.ValidateAsync(
                PRODUCT_CODE, licenseKey, machineId, Environment.MachineName);

            if (result.IsValid)
                SaveOfflineSnapshot(config, machineId, result);

            return result;
        }
        catch
        {
            // Portal unreachable — fall back to the offline grace-period
            // snapshot rather than blocking the agent on a network blip.
            return ValidateFromCache(config, machineId);
        }
    }

    private void SaveOfflineSnapshot(AgentConfig config, string machineId, LicenseValidationResult result)
    {
        config.LicenseLastValidatedOnUtc = DateTime.UtcNow.ToString("o");
        config.LicenseExpiresOnUtc = result.ExpiresOn?.ToString("o") ?? "";
        config.LicenseIsTrial = result.IsTrial;
        config.LicenseMachineId = machineId;
        _configSvc.Save(config);
    }

    private static LicenseValidationResult ValidateFromCache(AgentConfig config, string machineId)
    {
        if (string.IsNullOrEmpty(config.LicenseMachineId))
            return Fail("No local license cache found.");

        if (!string.Equals(config.LicenseMachineId, machineId, StringComparison.OrdinalIgnoreCase))
            return Fail("Cached license machine mismatch.");

        if (!DateTime.TryParse(config.LicenseLastValidatedOnUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastValidatedOnUtc))
            return Fail("Invalid license cache.");

        // System clock rolled back — guards against dodging expiry by
        // winding the clock back.
        if (DateTime.UtcNow < lastValidatedOnUtc)
            return Fail("System date/time tampering detected.");

        DateTime? expiresOnUtc = DateTime.TryParse(config.LicenseExpiresOnUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exp)
            ? exp
            : null;

        if (expiresOnUtc.HasValue && expiresOnUtc.Value.Date < DateTime.UtcNow.Date)
            return Fail("Cached license expired.");

        if ((DateTime.UtcNow - lastValidatedOnUtc).TotalDays > MAX_OFFLINE_DAYS)
            return Fail("Offline license validation window exceeded. Reconnect to the internet to re-validate.");

        return new LicenseValidationResult
        {
            IsValid = true,
            Message = "License valid (offline cache).",
            ExpiresOn = expiresOnUtc,
            IsTrial = config.LicenseIsTrial
        };
    }

    private static LicenseValidationResult Fail(string message) =>
        new() { IsValid = false, Message = message };
}
