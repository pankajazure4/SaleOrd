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
    public int RequestedCompanyId { get; set; }
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
