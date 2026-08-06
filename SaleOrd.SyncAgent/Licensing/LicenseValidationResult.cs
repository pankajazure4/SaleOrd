namespace SaleOrd.SyncAgent.Licensing;

public class LicenseValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? ExpiresOn { get; set; }
    public bool IsTrial { get; set; }
}
