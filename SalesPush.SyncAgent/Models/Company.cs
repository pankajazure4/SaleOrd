namespace SalesPush.SyncAgent.Models;

// Fetched from the Sales API (GET /api/companies) — maps a company record on
// the server side to the exact company name Tally has it loaded under
// locally. TallyCompanyName must match Tally's SVCURRENTCOMPANY exactly.
public class Company
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TallyCompanyName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
