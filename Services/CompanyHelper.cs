namespace SaleOrd.Services;

public static class CompanyHelper
{
    private const string CookieName = "SaleOrd_Co";

    public static int GetActiveCompanyId(HttpContext ctx, int defaultId, IEnumerable<int> validIds)
    {
        var validList = validIds as ICollection<int> ?? validIds.ToList();

        if (ctx.Request.Cookies.TryGetValue(CookieName, out var val)
            && int.TryParse(val, out var id)
            && validList.Contains(id))
            return id;

        if (validList.Contains(defaultId))
            return defaultId;

        // defaultId (the user's seeded/assigned company) is no longer an
        // accessible/active company — fall back to whatever company the
        // user can actually see, instead of resolving to a dead CompanyId
        // that silently returns zero rows on every scoped query.
        return validList.FirstOrDefault();
    }

    public static void SetActiveCompanyId(HttpContext ctx, int companyId)
    {
        ctx.Response.Cookies.Append(CookieName, companyId.ToString(), new CookieOptions
        {
            Expires  = DateTimeOffset.UtcNow.AddDays(30),
            SameSite = SameSiteMode.Lax,
            HttpOnly = true
        });
    }
}
