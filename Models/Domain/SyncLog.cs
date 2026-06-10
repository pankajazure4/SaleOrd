namespace SaleOrd.Models.Domain;

public class SyncLog
{
    public int SyncLogId { get; set; }
    public int CompanyId { get; set; }
    public string SyncType { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.Now;
}
