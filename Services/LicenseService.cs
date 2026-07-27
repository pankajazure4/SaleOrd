using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SaleOrd.Data;
using SaleOrd.Licensing;
using SaleOrd.Models.Domain;

namespace SaleOrd.Services;

// Single entry point for the license-gate middleware and the Settings page.
// Caches the portal result in-process for a short window (avoids a portal
// round trip on every request) and falls back to a DB-stored offline
// snapshot if the portal is unreachable.
public class LicenseService
{
    private const string PRODUCT_CODE = "SALEORD";
    private const string CACHE_KEY = "License:CheckResult";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private const int MAX_OFFLINE_DAYS = 7;

    private const string KeyLicenseKey = "License:Key";
    private const string KeyLastValidatedOnUtc = "License:LastValidatedOnUtc";
    private const string KeyExpiresOnUtc = "License:ExpiresOnUtc";
    private const string KeyIsTrial = "License:IsTrial";
    private const string KeyMaxUsers = "License:MaxUsers";
    private const string KeyMaxCompanies = "License:MaxCompanies";
    private const string KeyMachineId = "License:MachineId";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public LicenseService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public static string GetMachineId() => MachineFingerprint.GetUniqueMachineId();

    public async Task<string> GetLicenseKeyAsync()
    {
        var raw = await GetSettingAsync(KeyLicenseKey);
        return LicenseKeyProtector.Unprotect(raw);
    }

    public async Task SaveLicenseKeyAsync(string plainKey)
    {
        await SetSettingAsync(KeyLicenseKey, LicenseKeyProtector.Protect(plainKey.Trim()));
        await _db.SaveChangesAsync();
        Invalidate();
    }

    public void Invalidate() => _cache.Remove(CACHE_KEY);

    public async Task<LicenseValidationResult> CheckAsync()
    {
        if (_cache.TryGetValue(CACHE_KEY, out LicenseValidationResult? cached) && cached != null)
            return cached;

        await RefreshLock.WaitAsync();
        try
        {
            if (_cache.TryGetValue(CACHE_KEY, out cached) && cached != null)
                return cached;

            var result = await CheckUncachedAsync();
            _cache.Set(CACHE_KEY, result, CacheDuration);
            return result;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<LicenseValidationResult> CheckUncachedAsync()
    {
        var machineId = GetMachineId();
        var licenseKey = await GetLicenseKeyAsync();

        if (string.IsNullOrWhiteSpace(licenseKey))
            return Fail("License not configured. Go to Settings to activate.");

        try
        {
            var result = await LicensePortalClient.ValidateAsync(
                PRODUCT_CODE, licenseKey, machineId, Environment.MachineName);

            if (result.IsValid)
                await SaveOfflineSnapshotAsync(machineId, result);

            return result;
        }
        catch
        {
            // Portal unreachable — fall back to the offline grace-period
            // snapshot rather than locking the whole app on a network blip.
            return await ValidateFromCacheAsync(machineId);
        }
    }

    private async Task SaveOfflineSnapshotAsync(string machineId, LicenseValidationResult result)
    {
        await SetSettingAsync(KeyLastValidatedOnUtc, DateTime.UtcNow.ToString("o"));
        await SetSettingAsync(KeyExpiresOnUtc, result.ExpiresOn?.ToString("o") ?? "");
        await SetSettingAsync(KeyIsTrial, result.IsTrial.ToString());
        await SetSettingAsync(KeyMaxUsers, result.MaxUsers?.ToString() ?? "");
        await SetSettingAsync(KeyMaxCompanies, result.MaxCompanies?.ToString() ?? "");
        await SetSettingAsync(KeyMachineId, machineId);
        await _db.SaveChangesAsync();
    }

    private async Task<LicenseValidationResult> ValidateFromCacheAsync(string machineId)
    {
        var cachedMachineId = await GetSettingAsync(KeyMachineId);
        if (string.IsNullOrEmpty(cachedMachineId))
            return Fail("No local license cache found.");

        if (!string.Equals(cachedMachineId, machineId, StringComparison.OrdinalIgnoreCase))
            return Fail("Cached license machine mismatch.");

        var lastValidatedRaw = await GetSettingAsync(KeyLastValidatedOnUtc);
        if (!DateTime.TryParse(lastValidatedRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastValidatedOnUtc))
            return Fail("Invalid license cache.");

        // System clock rolled back — guards against dodging expiry by
        // winding the clock back, mirrors the desktop activator's check.
        if (DateTime.UtcNow < lastValidatedOnUtc)
            return Fail("System date/time tampering detected.");

        var expiresOnRaw = await GetSettingAsync(KeyExpiresOnUtc);
        DateTime? expiresOnUtc = DateTime.TryParse(expiresOnRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exp)
            ? exp
            : null;

        if (expiresOnUtc.HasValue && expiresOnUtc.Value.Date < DateTime.UtcNow.Date)
            return Fail("Cached license expired.");

        if ((DateTime.UtcNow - lastValidatedOnUtc).TotalDays > MAX_OFFLINE_DAYS)
            return Fail("Offline license validation window exceeded. Reconnect to the internet to re-validate.");

        var isTrialRaw = await GetSettingAsync(KeyIsTrial);
        var maxUsersRaw = await GetSettingAsync(KeyMaxUsers);
        var maxCompaniesRaw = await GetSettingAsync(KeyMaxCompanies);

        return new LicenseValidationResult
        {
            IsValid = true,
            Message = "License valid (offline cache).",
            ExpiresOn = expiresOnUtc,
            IsTrial = bool.TryParse(isTrialRaw, out var isTrial) && isTrial,
            MaxUsers = int.TryParse(maxUsersRaw, out var maxUsers) ? maxUsers : null,
            MaxCompanies = int.TryParse(maxCompaniesRaw, out var maxCompanies) ? maxCompanies : null
        };
    }

    private static LicenseValidationResult Fail(string message) =>
        new() { IsValid = false, Message = message };

    private async Task<string> GetSettingAsync(string key) =>
        await _db.AppSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync() ?? "";

    private async Task SetSettingAsync(string key, string value)
    {
        var setting = await _db.AppSettings.FindAsync(key);
        if (setting == null)
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else
            setting.Value = value;
    }
}
