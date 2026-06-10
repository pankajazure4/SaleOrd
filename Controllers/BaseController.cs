using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;

namespace SaleOrd.Controllers;

public class BaseController : Controller
{
    protected readonly AppDbContext Db;
    protected readonly UserManager<AppUser> UserMgr;

    public BaseController(AppDbContext db, UserManager<AppUser> userMgr)
    {
        Db = db;
        UserMgr = userMgr;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await UserMgr.GetUserAsync(User);
            if (user != null)
            {
                var userCompanies = await Db.UserCompanies
                    .Include(uc => uc.Company)
                    .Where(uc => uc.UserId == user.Id && uc.Company!.IsActive)
                    .Select(uc => uc.Company!)
                    .OrderBy(c => c.CompanyName)
                    .ToListAsync();

                ViewBag.UserCompanies = userCompanies;
                ViewBag.ActiveCompany = await Db.Companies.FindAsync(user.CompanyId);
            }
        }
        await next();
    }
}
