using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Models.ViewModels;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly TallyService _tally;
    private readonly SaleOrd.Services.PermissionService _permSvc;

    public AdminController(AppDbContext db, UserManager<AppUser> userManager, TallyService tally, SaleOrd.Services.PermissionService permSvc)
    {
        _db = db;
        _userManager = userManager;
        _tally = tally;
        _permSvc = permSvc;
    }

    // Companies
    public async Task<IActionResult> Companies()
    {
        var companies = await _db.Companies.OrderBy(c => c.CompanyName).ToListAsync();
        return View(companies);
    }

    [HttpGet]
    public IActionResult CreateCompany() => View(new CompanyVM());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCompany(CompanyVM model)
    {
        if (!ModelState.IsValid) return View(model);

        _db.Companies.Add(new Company
        {
            CompanyName = model.CompanyName,
            TallyIp = model.TallyIp,
            TallyPort = model.TallyPort,
            TallyCompanyName = model.TallyCompanyName,
            IsActive = model.IsActive
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Company added.";
        return RedirectToAction("Companies");
    }

    [HttpGet]
    public async Task<IActionResult> EditCompany(int id)
    {
        var c = await _db.Companies.FindAsync(id);
        if (c == null) return NotFound();
        return View(new CompanyVM
        {
            CompanyId = c.CompanyId,
            CompanyName = c.CompanyName,
            TallyIp = c.TallyIp,
            TallyPort = c.TallyPort,
            TallyCompanyName = c.TallyCompanyName,
            IsActive = c.IsActive
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCompany(CompanyVM model)
    {
        if (!ModelState.IsValid) return View(model);

        var c = await _db.Companies.FindAsync(model.CompanyId);
        if (c == null) return NotFound();

        c.CompanyName = model.CompanyName;
        c.TallyIp = model.TallyIp;
        c.TallyPort = model.TallyPort;
        c.TallyCompanyName = model.TallyCompanyName;
        c.IsActive = model.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Company updated.";
        return RedirectToAction("Companies");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PingTally(int id)
    {
        var c = await _db.Companies.FindAsync(id);
        if (c == null) return NotFound();

        var url = $"http://{c.TallyIp}:{c.TallyPort}";
        var (ok, _) = await _tally.CheckStatusAsync(url);
        return Json(new { success = ok, message = ok ? "Tally is reachable" : "Cannot connect to Tally" });
    }

    // Users
    public async Task<IActionResult> Users()
    {
        var users = await _db.Users
            .Include(u => u.UserCompanies).ThenInclude(uc => uc.Company)
            .OrderBy(u => u.FullName)
            .Select(u => new UserListVM
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? string.Empty,
                Role = u.Role,
                Companies = string.Join(", ", u.UserCompanies.Select(uc => uc.Company!.CompanyName)),
                IsActive = u.IsActive
            })
            .ToListAsync();
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> CreateUser()
    {
        ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
        return View(new UserCreateVM());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(UserCreateVM model)
    {
        if (model.CompanyIds == null || !model.CompanyIds.Any())
            ModelState.AddModelError("CompanyIds", "Select at least one company.");

        if (!ModelState.IsValid)
        {
            ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
            return View(model);
        }

        var user = new AppUser
        {
            FullName = model.FullName,
            Email = model.Email,
            UserName = model.Email,
            CompanyId = model.DefaultCompanyId,
            Role = model.Role
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, model.Role);

            foreach (var cId in model.CompanyIds.Distinct())
                _db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = cId });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"User {model.FullName} created with {model.CompanyIds.Count} company access.";
            return RedirectToAction("Users");
        }

        foreach (var e in result.Errors)
            ModelState.AddModelError("", e.Description);

        ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(string id)
    {
        var user = await _db.Users
            .Include(u => u.UserCompanies)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();

        var model = new UserEditVM
        {
            Id              = user.Id,
            FullName        = user.FullName,
            Email           = user.Email ?? string.Empty,
            Role            = user.Role,
            DefaultCompanyId = user.CompanyId,
            CompanyIds      = user.UserCompanies.Select(uc => uc.CompanyId).ToList(),
            IsActive        = user.IsActive
        };
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(UserEditVM model)
    {
        // Remove password validation for edit (it's optional)
        ModelState.Remove(nameof(model.NewPassword));
        if (!ModelState.IsValid)
        {
            ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
            return View(model);
        }

        var user = await _db.Users
            .Include(u => u.UserCompanies)
            .FirstOrDefaultAsync(u => u.Id == model.Id);
        if (user == null) return NotFound();

        user.FullName  = model.FullName;
        user.Role      = model.Role;
        user.IsActive  = model.IsActive;
        user.CompanyId = model.DefaultCompanyId;

        // Update password if provided
        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                    ModelState.AddModelError("", e.Description);
                ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
                return View(model);
            }
        }

        // Update role
        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, model.Role);

        // Update company mappings
        _db.UserCompanies.RemoveRange(user.UserCompanies);
        var companyIds = (model.CompanyIds ?? new List<int>()).Distinct().ToList();
        // Always include default company
        if (!companyIds.Contains(model.DefaultCompanyId))
            companyIds.Add(model.DefaultCompanyId);

        foreach (var cId in companyIds)
            _db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = cId });

        await _userManager.UpdateAsync(user);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"{model.FullName} updated.";
        return RedirectToAction("Users");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        TempData["Success"] = user.IsActive ? "User activated." : "User deactivated.";
        return RedirectToAction("Users");
    }

    // Role Rights
    [HttpGet]
    public async Task<IActionResult> RoleRights()
    {
        var roles = new[] { AppRoles.Manager, AppRoles.Salesman };

        var existing = await _db.RolePermissions
            .Where(p => roles.Contains(p.Role))
            .ToListAsync();

        var matrix = new Dictionary<string, Dictionary<string, bool>>();
        foreach (var role in roles)
        {
            matrix[role] = new Dictionary<string, bool>();
            foreach (var (key, _, _) in AppPermissions.All)
            {
                var perm = existing.FirstOrDefault(p => p.Role == role && p.PermissionKey == key);
                matrix[role][key] = perm?.IsAllowed ?? true;
            }
        }

        return View(new RoleRightsVM { Matrix = matrix, Roles = roles });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RoleRights(IFormCollection form)
    {
        var roles = new[] { AppRoles.Manager, AppRoles.Salesman };

        var existing = await _db.RolePermissions
            .Where(p => roles.Contains(p.Role))
            .ToListAsync();

        foreach (var role in roles)
        {
            foreach (var (key, _, _) in AppPermissions.All)
            {
                var formKey = $"p_{role}_{key.Replace(".", "_")}";
                var isAllowed = form.ContainsKey(formKey) && form[formKey] == "1";

                var perm = existing.FirstOrDefault(p => p.Role == role && p.PermissionKey == key);
                if (perm == null)
                    _db.RolePermissions.Add(new RolePermission { Role = role, PermissionKey = key, IsAllowed = isAllowed });
                else
                    perm.IsAllowed = isAllowed;
            }
        }

        await _db.SaveChangesAsync();
        _permSvc.InvalidateCache();

        TempData["Success"] = "Role rights updated.";
        return RedirectToAction(nameof(RoleRights));
    }

    // User Activity
    public async Task<IActionResult> UserActivity(string? userId, string? action, DateTime? from, DateTime? to, int page = 1)
    {
        var query = _db.UserActivities.AsQueryable();

        if (!string.IsNullOrEmpty(userId))  query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrEmpty(action))  query = query.Where(a => a.Action == action);
        if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value.Date);
        if (to.HasValue)   query = query.Where(a => a.CreatedAt < to.Value.Date.AddDays(1));

        const int pageSize = 50;
        var total = await query.CountAsync();
        var logs  = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Total    = total;
        ViewBag.Page     = page;
        ViewBag.PageSize = pageSize;
        ViewBag.UserId   = userId;
        ViewBag.Action   = action;
        ViewBag.From     = from?.ToString("yyyy-MM-dd");
        ViewBag.To       = to?.ToString("yyyy-MM-dd");
        ViewBag.UserList = await _db.Users.OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync();
        ViewBag.Actions  = new[] {
            ActivityActions.Login, ActivityActions.Logout,
            ActivityActions.CreateOrder, ActivityActions.EditOrder, ActivityActions.CancelOrder,
            ActivityActions.ResubmitOrder, ActivityActions.PrintOrder, ActivityActions.SyncTriggered
        };

        return View(logs);
    }

    // Sync logs
    public async Task<IActionResult> SyncLogs(int? companyId)
    {
        var query = _db.SyncLogs.AsQueryable();
        if (companyId.HasValue) query = query.Where(l => l.CompanyId == companyId);

        var logs = await query
            .OrderByDescending(l => l.SyncedAt)
            .Take(200)
            .ToListAsync();

        ViewBag.Companies = await _db.Companies.ToListAsync();
        ViewBag.SelectedCompany = companyId;
        return View(logs);
    }
}
