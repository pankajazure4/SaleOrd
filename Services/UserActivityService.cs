using SaleOrd.Data;
using SaleOrd.Models.Domain;

namespace SaleOrd.Services;

public class UserActivityService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _ctx;

    public UserActivityService(AppDbContext db, IHttpContextAccessor ctx)
    {
        _db = db;
        _ctx = ctx;
    }

    public async Task LogAsync(string userId, string userName, string role, string action,
        string? entityType = null, int? entityId = null, string? description = null, int? companyId = null)
    {
        var ip = _ctx.HttpContext?.Connection.RemoteIpAddress?.ToString();
        _db.UserActivities.Add(new UserActivity
        {
            UserId      = userId,
            UserName    = userName,
            Role        = role,
            Action      = action,
            EntityType  = entityType,
            EntityId    = entityId,
            Description = description,
            CompanyId   = companyId,
            IPAddress   = ip,
            CreatedAt   = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }
}
