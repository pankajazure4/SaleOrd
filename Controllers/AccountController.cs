using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Models.ViewModels;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;
    private readonly UserActivityService _activity;

    public AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> userManager,
        AppDbContext db, UserActivityService activity)
    {
        _signIn = signIn;
        _userManager = userManager;
        _db = db;
        _activity = activity;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (_signIn.IsSignedIn(User)) return RedirectToAction("Index", "Dashboard");
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVM model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _signIn.PasswordSignInAsync(model.Email, model.Password,
            model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user is { IsActive: false })
            {
                await _signIn.SignOutAsync();
                ModelState.AddModelError("", "Your account has been deactivated.");
                return View(model);
            }
            if (user != null)
                await _activity.LogAsync(user.Id, user.FullName, user.Role, ActivityActions.Login,
                    companyId: user.CompanyId);
            return LocalRedirect(model.ReturnUrl ?? "/Dashboard");
        }

        if (result.IsLockedOut)
            ModelState.AddModelError("", "Account locked. Try again after 5 minutes.");
        else
            ModelState.AddModelError("", "Invalid email or password.");

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user != null)
            await _activity.LogAsync(user.Id, user.FullName, user.Role, ActivityActions.Logout,
                companyId: user.CompanyId);
        await _signIn.SignOutAsync();
        return RedirectToAction("Login");
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> SwitchCompany(int companyId, string? returnUrl)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        // Admin can switch to any active company; others only their mapped ones
        bool allowed;
        if (user.Role == AppRoles.Admin)
            allowed = await _db.Companies.AnyAsync(c => c.CompanyId == companyId && c.IsActive);
        else
            allowed = await _db.UserCompanies.AnyAsync(uc => uc.UserId == user.Id && uc.CompanyId == companyId);

        if (!allowed) return Forbid();

        CompanyHelper.SetActiveCompanyId(HttpContext, companyId);

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
