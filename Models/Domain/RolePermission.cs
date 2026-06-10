namespace SaleOrd.Models.Domain;

public class RolePermission
{
    public int Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string PermissionKey { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
}
