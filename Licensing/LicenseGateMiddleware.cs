using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SaleOrd.Services;

namespace SaleOrd.Licensing;

// Hard app-wide gate: everything except Account (Login/Logout, so an admin
// can always sign in) and Settings (Admin-only, needed to activate the
// license) is blocked until LicenseService.CheckAsync() reports valid.
// Registered after UseAuthorization() so it only ever runs for requests that
// already passed authentication/authorization — anonymous access to a
// protected endpoint is already redirected to Login before reaching here.
public class LicenseGateMiddleware
{
    private readonly RequestDelegate _next;

    public LicenseGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, LicenseService licenseService)
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/Account") || path.StartsWithSegments("/Settings"))
        {
            await _next(context);
            return;
        }

        var result = await licenseService.CheckAsync();
        if (result.IsValid)
        {
            await _next(context);
            return;
        }

        bool isAjax = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        if (isAjax)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                success = false,
                message = "This application is not licensed. " + result.Message
            }));
            return;
        }

        context.Response.Redirect("/Account/LicenseRequired");
    }
}
