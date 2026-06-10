using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.ViewComponents;

public class CompanySwitcherViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public CompanySwitcherViewComponent(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal?.Identity?.IsAuthenticated != true)
            return Content(string.Empty);

        var user = await _userManager.GetUserAsync(UserClaimsPrincipal);
        if (user == null) return Content(string.Empty);

        List<Company> companies;
        if (user.Role == AppRoles.Admin)
        {
            companies = await _db.Companies.Where(c => c.IsActive)
                .OrderBy(c => c.CompanyName).ToListAsync();
        }
        else
        {
            var ids = await _db.UserCompanies
                .Where(uc => uc.UserId == user.Id)
                .Select(uc => uc.CompanyId).ToListAsync();
            if (!ids.Any()) ids = new List<int> { user.CompanyId };
            companies = await _db.Companies
                .Where(c => c.IsActive && ids.Contains(c.CompanyId))
                .OrderBy(c => c.CompanyName).ToListAsync();
        }

        if (!companies.Any()) return Content(string.Empty);

        var validIds  = companies.Select(c => c.CompanyId).ToList();
        var activeId  = CompanyHelper.GetActiveCompanyId(HttpContext, user.CompanyId, validIds);
        var active    = companies.FirstOrDefault(c => c.CompanyId == activeId) ?? companies.First();

        return View(new CompanySwitcherVM { Companies = companies, Active = active });
    }
}

public class CompanySwitcherVM
{
    public List<Company> Companies { get; set; } = new();
    public Company? Active { get; set; }
}
