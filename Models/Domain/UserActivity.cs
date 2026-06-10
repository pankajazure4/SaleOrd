namespace SaleOrd.Models.Domain;

public class UserActivity
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;   // Login, Logout, CreateOrder, EditOrder, PrintOrder, etc.
    public string? EntityType { get; set; }               // SaleOrder, Company, etc.
    public int? EntityId { get; set; }
    public string? Description { get; set; }
    public int? CompanyId { get; set; }
    public string? IPAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class ActivityActions
{
    public const string Login          = "Login";
    public const string Logout         = "Logout";
    public const string CreateOrder    = "CreateOrder";
    public const string EditOrder      = "EditOrder";
    public const string CancelOrder    = "CancelOrder";
    public const string ResubmitOrder  = "ResubmitOrder";
    public const string PrintOrder     = "PrintOrder";
    public const string SyncTriggered  = "SyncTriggered";
}
