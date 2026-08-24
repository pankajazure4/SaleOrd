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

// Public self-signup form — no role/company-access fields here on purpose.
// Role and final company access are Admin-only decisions made at approval
// time (AdminController.ApproveSignup), not something a signee picks for
// themselves. Password IS collected here though — the signer sets their own
// at signup rather than getting handed a default one after approval (see
// SignupRequest.PasswordHash); [Required]/[MinLength] here are just the
// client-visible half, the real rule enforced server-side is Identity's own
// configured policy (Program.cs) via AccountController.SignUp's
// PasswordValidators check, so the two must be kept in sync by hand.
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

    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your password")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class SignupRequestListVM
{
    public int SignupRequestId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string OrganizationName { get; set; } = string.Empty;

    // False only for requests submitted before Sign Up collected a password
    // (see SignupRequest.PasswordHash) — AdminController.ApproveSignup
    // refuses to approve those, so surface it here to warn before they try.
    public bool HasPassword { get; set; }

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
    public string? OutletName { get; set; }
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

    // Set by SaleOrd.SyncAgent once it has actually pushed this party into
    // Tally as a new ledger master — distinct from LastSyncedAt (which the
    // regular Tally master sync touches for every ledger). Null means
    // Approved-but-not-pushed-yet, or still Pending/Rejected.
    public DateTime? TallyPushedAt { get; set; }
}
