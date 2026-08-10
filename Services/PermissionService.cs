using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SaleOrd.Data;
using SaleOrd.Models.Domain;

namespace SaleOrd.Services;

public class PermissionService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    // Roles are no longer a fixed {Manager, Salesman} pair — Admin can create
    // more of them at runtime (see AdminController.CreateRole) — so cache
    // keys can't be invalidated by iterating a hardcoded role list anymore.
    // A version stamp baked into every key sidesteps that: bumping it makes
    // every previously-cached entry, for any role, unreachable at once; the
    // old entries just age out on their own 10-minute TTL.
    private static int _cacheVersion;

    public PermissionService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<HashSet<string>> GetAllowedKeysAsync(string role)
    {
        if (role == AppRoles.Admin)
            return AppPermissions.All.Select(p => p.Key).ToHashSet();

        var cacheKey = $"perms_v{_cacheVersion}_{role}";
        if (_cache.TryGetValue(cacheKey, out HashSet<string>? cached) && cached != null)
            return cached;

        var allowed = await _db.RolePermissions
            .Where(p => p.Role == role && p.IsAllowed)
            .Select(p => p.PermissionKey)
            .ToListAsync();

        var result = allowed.ToHashSet();
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }

    public async Task<bool> HasAsync(string role, string permissionKey)
    {
        if (role == AppRoles.Admin) return true;
        var keys = await GetAllowedKeysAsync(role);
        return keys.Contains(permissionKey);
    }

    public void InvalidateCache()
    {
        Interlocked.Increment(ref _cacheVersion);
    }
}
