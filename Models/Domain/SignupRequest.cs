namespace SaleOrd.Models.Domain;

// A user-submitted request for an account, created by the public Sign Up
// form — no AppUser/Identity account exists yet at this point. An Admin
// reviews it in Admin > Signup Requests; only on Approve does the actual
// AppUser get created (see AdminController.ApproveSignup), so unreviewed or
// rejected requests never consume a login.
public class SignupRequest
{
    public int SignupRequestId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }

    // The signer's own chosen password, already hashed at Sign Up time
    // (AccountController.SignUp, via IPasswordHasher<AppUser>) — never
    // stored in plain text, and never re-entered by anyone. On approval
    // (AdminController.ApproveSignup) this hash is copied straight onto the
    // new AppUser, so the user logs in with the password they set here, not
    // a default one an Admin has to hand them separately.
    public string PasswordHash { get; set; } = string.Empty;

    // Free text the signer types themselves — "which company/organization do
    // you belong to" — not a pick from our own internal Companies (Tally
    // territories) list. That assignment is now an Admin-only decision made
    // at approval time (AdminController.ApproveSignup's companyId param),
    // same as Role already was.
    public string OrganizationName { get; set; } = string.Empty;

    // Nullable now — no longer collected on the public Sign Up form. Kept
    // only so older rows (from before this field existed) still show what
    // was picked back then; new requests always leave this null.
    public int? RequestedCompanyId { get; set; }
    public SignupRequestStatus Status { get; set; } = SignupRequestStatus.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.Now;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedById { get; set; }
    public string? RejectionReason { get; set; }

    public Company? RequestedCompany { get; set; }
    public AppUser? ReviewedBy { get; set; }
}

public enum SignupRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
