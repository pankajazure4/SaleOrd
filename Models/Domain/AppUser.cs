using Microsoft.AspNetCore.Identity;

namespace SaleOrd.Models.Domain;

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string Role { get; set; } = AppRoles.Salesman;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Company? Company { get; set; }
    public ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
}

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Salesman = "Salesman";
}
