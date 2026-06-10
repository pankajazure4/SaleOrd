namespace SaleOrd.Models.Domain;

public class Company
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TallyIp { get; set; } = "localhost";
    public int TallyPort { get; set; } = 9000;
    public string TallyCompanyName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastMasterSyncAt { get; set; }

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Ledger> Ledgers { get; set; } = new List<Ledger>();
    public ICollection<StockItem> StockItems { get; set; } = new List<StockItem>();
    public ICollection<Godown> Godowns { get; set; } = new List<Godown>();
    public ICollection<SaleOrder> SaleOrders { get; set; } = new List<SaleOrder>();
}
