namespace SaleOrd.SyncAgent.Models;

public class Company
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TallyIp { get; set; } = "localhost";
    public int TallyPort { get; set; } = 9000;
    public string TallyCompanyName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastMasterSyncAt { get; set; }
}
