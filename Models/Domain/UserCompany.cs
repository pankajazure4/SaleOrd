namespace SaleOrd.Models.Domain;

public class UserCompany
{
    public int UserCompanyId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int CompanyId { get; set; }

    public AppUser? User { get; set; }
    public Company? Company { get; set; }
}
