namespace SaleOrd.SyncAgent.Models;

public class Godown
{
    public int GodownId { get; set; }
    public string GodownName { get; set; } = string.Empty;
    public string? Parent { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PinCode { get; set; }
    public bool IsBatchwiseOn { get; set; }

    public string? GUID { get; set; }
    public long AlterId { get; set; }

    public int CompanyId { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
