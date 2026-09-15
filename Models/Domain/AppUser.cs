using Microsoft.AspNetCore.Identity;

namespace SaleOrd.Models.Domain;

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string Role { get; set; } = AppRoles.Salesman;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Flat hierarchy only — a Salesman's Manager, set at Signup approval or
    // from Admin > Edit User. Managers/Admins leave this null (a Manager
    // never themselves reports to another Manager in this model). Used by
    // UserVisibility to scope a Manager's dashboard/reports to their own
    // team instead of the whole company.
    public string? ManagerId { get; set; }

    public Company? Company { get; set; }
    public AppUser? Manager { get; set; }
    public ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
}

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Salesman = "Salesman";
}
