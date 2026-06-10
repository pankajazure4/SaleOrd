using System.ComponentModel.DataAnnotations;
using SaleOrd.Models.Domain;

namespace SaleOrd.Models.ViewModels;

public class CompanyVM
{
    public int CompanyId { get; set; }

    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    public string TallyIp { get; set; } = "localhost";

    [Required, Range(1, 65535)]
    public int TallyPort { get; set; } = 9000;

    [Required, MaxLength(200)]
    public string TallyCompanyName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class UserCreateVM
{
    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = AppRoles.Salesman;

    [Required(ErrorMessage = "Select default company")]
    public int DefaultCompanyId { get; set; }

    public List<int> CompanyIds { get; set; } = new();
}

public class UserListVM
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Companies { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class RoleRightsVM
{
    // role -> permKey -> isAllowed
    public Dictionary<string, Dictionary<string, bool>> Matrix { get; set; } = new();
    public string[] Roles { get; set; } = Array.Empty<string>();
}

public class UserEditVM
{
    public string Id { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = AppRoles.Salesman;

    public int DefaultCompanyId { get; set; }
    public List<int> CompanyIds { get; set; } = new();
    public bool IsActive { get; set; } = true;

    [MinLength(6), DataType(DataType.Password)]
    public string? NewPassword { get; set; }
}
