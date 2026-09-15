namespace SaleOrd.Models.Domain;

// A small, Admin-managed fixed list — populates the "Zone" dropdown an
// Admin picks from at Party Approval (Admin > Pending Parties). Per-company
// on purpose, same as most masters here: a zone that makes sense for one
// client's company/territory may not for another.
public class Zone
{
    public int ZoneId { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public bool IsActive { get; set; } = true;

    public Company? Company { get; set; }
}
