using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;

namespace SaleOrd.Services;

/// <summary>
/// Resolves the active company for the current user.
/// Priority: cookie → user.CompanyId fallback
/// Admin sees all active companies; others see only mapped ones.
/// </summary>
public class ActiveCompanyResolver
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IHttpContextAccessor _ctx;

    public ActiveCompanyResolver(AppDbContext db, UserManager<AppUser> userManager, IHttpContextAccessor ctx)
    {
        _db = db;
        _userManager = userManager;
        _ctx = ctx;
    }

    public async Task<(int CompanyId, AppUser? User)> ResolveAsync()
    {
        var httpCtx = _ctx.HttpContext!;
        var user = await _userManager.GetUserAsync(httpCtx.User);
        if (user == null) return (0, null);

        var validIds = await GetAccessibleIdsAsync(user);
        var activeId = CompanyHelper.GetActiveCompanyId(httpCtx, user.CompanyId, validIds);
        return (activeId, user);
    }

    public async Task<List<int>> GetAccessibleIdsAsync(AppUser user)
    {
        if (user.Role == AppRoles.Admin)
            return await _db.Companies.Where(c => c.IsActive).Select(c => c.CompanyId).ToListAsync();

        var ids = await _db.UserCompanies
            .Where(uc => uc.UserId == user.Id)
            .Select(uc => uc.CompanyId)
            .ToListAsync();

        return ids.Any() ? ids : new List<int> { user.CompanyId };
    }
}
