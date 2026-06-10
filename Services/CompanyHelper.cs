namespace SaleOrd.Services;

public static class CompanyHelper
{
    private const string CookieName = "SaleOrd_Co";

    public static int GetActiveCompanyId(HttpContext ctx, int defaultId, IEnumerable<int> validIds)
    {
        if (ctx.Request.Cookies.TryGetValue(CookieName, out var val)
            && int.TryParse(val, out var id)
            && validIds.Contains(id))
            return id;
        return defaultId;
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
