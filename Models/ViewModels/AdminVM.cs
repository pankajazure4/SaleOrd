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

// Public self-signup form — no password/role/company-access fields here on
// purpose. Role and final company access are Admin-only decisions made at
// approval time (AdminController.ApproveSignup), not something a signee
// picks for themselves.
public class SignUpVM
{
    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Enter a valid 10-digit mobile number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your company / organization name"), MaxLength(200)]
    public string OrganizationName { get; set; } = string.Empty;
}

public class SignupRequestListVM
{
    public int SignupRequestId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string OrganizationName { get; set; } = string.Empty;

    // Legacy-only — null for every request submitted after the Company
    // dropdown was removed from Sign Up; only pre-existing rows still carry
    // these (see SignupRequest.RequestedCompanyId). RequestedCompanyId is
    // kept around just to pre-select a sensible default in the Approve
    // modal's company picker for those older rows.
    public string? RequestedCompanyName { get; set; }
    public int? RequestedCompanyId { get; set; }
    public SignupRequestStatus Status { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByName { get; set; }
    public string? RejectionReason { get; set; }
}

public class RoleListItemVM
{
    public string Name { get; set; } = string.Empty;
    public bool IsProtected { get; set; }
    public int UserCount { get; set; }
}

public class PartyApprovalListVM
{
    public int LedgerId { get; set; }
    public string LedgerName { get; set; } = string.Empty;
    public string? GSTNo { get; set; }
    public string? FSSAINo { get; set; }
    public bool HasFssaiDocument { get; set; }
    public string? Address { get; set; }
    public string? State { get; set; }
    public string? MobileNo { get; set; }
    public LedgerApprovalStatus ApprovalStatus { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByName { get; set; }
    public string? RejectionReason { get; set; }
}
