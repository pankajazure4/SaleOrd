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
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly TallyService _tally;
    private readonly SaleOrd.Services.PermissionService _permSvc;

    public AdminController(AppDbContext db, UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager,
        TallyService tally, SaleOrd.Services.PermissionService permSvc)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _tally = tally;
        _permSvc = permSvc;
    }

    // Source of truth for "what roles exist" is ASP.NET Identity's own
    // AspNetRoles table (via RoleManager) — no separate custom Roles table
    // needed, it's already exactly that. Admin/Manager/Salesman always sort
    // first (in that order) since they're the built-in roles everyone
    // recognizes; any roles an Admin creates later sort alphabetically after.
    private async Task<List<string>> GetAllRoleNamesAsync()
    {
        var builtIn = new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Salesman };
        var all = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
        var custom = all.Except(builtIn).OrderBy(r => r);
        return builtIn.Where(all.Contains).Concat(custom).ToList();
    }

    private static bool IsProtectedRole(string role) =>
        role is AppRoles.Admin or AppRoles.Manager or AppRoles.Salesman;

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
        ViewBag.Roles = await GetAllRoleNamesAsync();
        return View(new UserCreateVM());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(UserCreateVM model)
    {
        if (model.CompanyIds == null || !model.CompanyIds.Any())
            ModelState.AddModelError("CompanyIds", "Select at least one company.");

        if (!string.IsNullOrEmpty(model.Role) && !await _roleManager.RoleExistsAsync(model.Role))
            ModelState.AddModelError("Role", "Select a valid role.");

        if (!ModelState.IsValid)
        {
            ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
            ViewBag.Roles = await GetAllRoleNamesAsync();
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
        ViewBag.Roles = await GetAllRoleNamesAsync();
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
        ViewBag.Roles = await GetAllRoleNamesAsync();

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

        if (!string.IsNullOrEmpty(model.Role) && !await _roleManager.RoleExistsAsync(model.Role))
            ModelState.AddModelError("Role", "Select a valid role.");

        if (!ModelState.IsValid)
        {
            ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
            ViewBag.Roles = await GetAllRoleNamesAsync();
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
                ViewBag.Roles = await GetAllRoleNamesAsync();
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

    // Signup Requests — see SignupRequest for the why. Approving here is the
    // only place a self-signup actually turns into a real AppUser login.
    public async Task<IActionResult> SignupRequests()
    {
        var requests = await _db.SignupRequests
            .Include(r => r.RequestedCompany)
            .Include(r => r.ReviewedBy)
            .OrderBy(r => r.Status) // Pending (0) first
            .ThenByDescending(r => r.RequestedAt)
            .Select(r => new SignupRequestListVM
            {
                SignupRequestId = r.SignupRequestId,
                FullName = r.FullName,
                Email = r.Email,
                PhoneNumber = r.PhoneNumber,
                OrganizationName = r.OrganizationName,
                HasPassword = r.PasswordHash != "",
                RequestedCompanyName = r.RequestedCompany != null ? r.RequestedCompany.CompanyName : null,
                RequestedCompanyId = r.RequestedCompanyId,
                Status = r.Status,
                RequestedAt = r.RequestedAt,
                ReviewedAt = r.ReviewedAt,
                ReviewedByName = r.ReviewedBy != null ? r.ReviewedBy.FullName : null,
                RejectionReason = r.RejectionReason
            })
            .ToListAsync();

        ViewBag.Roles = await GetAllRoleNamesAsync();
        ViewBag.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
        return View(requests);
    }

    // companyId is now an Admin-only decision made here at approval — the
    // signer only ever typed a free-text OrganizationName on the public
    // form, they never picked from our internal Companies list.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveSignup(int id, string role, int companyId)
    {
        var request = await _db.SignupRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != SignupRequestStatus.Pending)
        {
            TempData["Error"] = "This request has already been reviewed.";
            return RedirectToAction(nameof(SignupRequests));
        }
        if (string.IsNullOrEmpty(role) || !await _roleManager.RoleExistsAsync(role))
        {
            TempData["Error"] = "Select a valid role.";
            return RedirectToAction(nameof(SignupRequests));
        }
        if (!await _db.Companies.AnyAsync(c => c.CompanyId == companyId && c.IsActive))
        {
            TempData["Error"] = "Select a valid company.";
            return RedirectToAction(nameof(SignupRequests));
        }
        if (string.IsNullOrEmpty(request.PasswordHash))
        {
            // Requests submitted before self-chosen passwords existed on the
            // Sign Up form have no hash to carry over — approving as-is would
            // silently create a login nobody can ever sign into. Reject it
            // and have them resubmit through the current form instead.
            TempData["Error"] = "This request predates password-at-signup and has none saved — reject it and ask the user to sign up again.";
            return RedirectToAction(nameof(SignupRequests));
        }

        var admin = await _userManager.GetUserAsync(User);

        // PasswordHash carries straight over from what the user set at Sign
        // Up (AccountController.SignUp) — CreateAsync(user), the no-password
        // overload, persists the object as given rather than hashing a new
        // one, so this is the one and only password they'll know: no default
        // for an Admin to generate or communicate.
        var user = new AppUser
        {
            FullName = request.FullName,
            Email = request.Email,
            UserName = request.Email,
            PhoneNumber = request.PhoneNumber,
            CompanyId = companyId,
            Role = role,
            PasswordHash = request.PasswordHash
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(SignupRequests));
        }

        await _userManager.AddToRoleAsync(user, role);
        _db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = companyId });

        request.Status = SignupRequestStatus.Approved;
        request.ReviewedAt = DateTime.Now;
        request.ReviewedById = admin?.Id;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"{request.FullName} approved — they can log in with {request.Email} and the password they set at signup " +
            "(more companies or a password reset can be done from Edit User).";
        return RedirectToAction(nameof(SignupRequests));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectSignup(int id, string? reason)
    {
        var request = await _db.SignupRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != SignupRequestStatus.Pending)
        {
            TempData["Error"] = "This request has already been reviewed.";
            return RedirectToAction(nameof(SignupRequests));
        }

        var admin = await _userManager.GetUserAsync(User);
        request.Status = SignupRequestStatus.Rejected;
        request.ReviewedAt = DateTime.Now;
        request.ReviewedById = admin?.Id;
        request.RejectionReason = reason;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Signup request from {request.FullName} rejected.";
        return RedirectToAction(nameof(SignupRequests));
    }

    // Party Master approval — a party created via Masters > Parties sits
    // Pending until reviewed here; only on approval does it become pickable
    // in Sale Orders (SaleOrderController.GetParties filters by
    // ApprovalStatus). The actual Tally push happens later, out-of-band, in
    // SaleOrd.SyncAgent (see its manual-party-push step) — the web app has
    // no direct route to the client's Tally instance (TallySync:Mode=Agent),
    // so this screen only ever shows/approves the manually-created parties
    // and never talks to Tally itself.
    //
    // IsManuallyCreated filters out the thousands of ordinary Tally-synced
    // ledgers, which have nothing to do with this approval workflow.
    public async Task<IActionResult> PendingParties(string? q)
    {
        var query = _db.Ledgers
            .Include(l => l.ReviewedBy)
            .Where(l => l.IsManuallyCreated);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(l =>
                EF.Functions.Like(l.LedgerName, $"%{term}%") ||
                (l.OutletName != null && EF.Functions.Like(l.OutletName, $"%{term}%")) ||
                (l.GSTNo != null && EF.Functions.Like(l.GSTNo, $"%{term}%")) ||
                (l.MobileNo != null && EF.Functions.Like(l.MobileNo, $"%{term}%")));
        }

        var parties = await query
            .OrderBy(l => l.ApprovalStatus == LedgerApprovalStatus.Pending ? 0 : 1) // Pending first
            .ThenByDescending(l => l.LastSyncedAt)
            .Select(l => new PartyApprovalListVM
            {
                LedgerId = l.LedgerId,
                LedgerName = l.LedgerName,
                OutletName = l.OutletName,
                GSTNo = l.GSTNo,
                FSSAINo = l.FSSAINo,
                HasFssaiDocument = l.FSSAIDocumentPath != null,
                Address = l.Address,
                State = l.State,
                MobileNo = l.MobileNo,
                ApprovalStatus = l.ApprovalStatus,
                LastSyncedAt = l.LastSyncedAt,
                ReviewedAt = l.ReviewedAt,
                ReviewedByName = l.ReviewedBy != null ? l.ReviewedBy.FullName : null,
                RejectionReason = l.RejectionReason,
                TallyPushedAt = l.TallyPushedAt
            })
            .ToListAsync();

        ViewBag.SearchTerm = q;
        return View(parties);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveParty(int id)
    {
        var ledger = await _db.Ledgers.Include(l => l.Company).FirstOrDefaultAsync(l => l.LedgerId == id);
        if (ledger == null) return NotFound();
        if (ledger.ApprovalStatus != LedgerApprovalStatus.Pending)
        {
            TempData["Error"] = "This party has already been reviewed.";
            return RedirectToAction(nameof(PendingParties));
        }
        if (ledger.Company == null)
        {
            TempData["Error"] = "This party's company could not be found.";
            return RedirectToAction(nameof(PendingParties));
        }

        var admin = await _userManager.GetUserAsync(User);

        // No Tally push here — approving only makes the party usable in Sale
        // Orders locally. SaleOrd.SyncAgent picks up Approved + IsManuallyCreated
        // + TallyPushedAt == null ledgers on its own cycle and pushes them to
        // Tally, exactly like it already does for Sale Orders; it logs that
        // attempt itself (SyncType "PartyApproval") so the trace still lands
        // in Admin > Sync Logs, just from the side that actually talks to Tally.
        ledger.ApprovalStatus = LedgerApprovalStatus.Approved;
        ledger.ReviewedAt = DateTime.Now;
        ledger.ReviewedById = admin?.Id;
        ledger.RejectionReason = null;

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Party \"{ledger.LedgerName}\" approved and is now usable in Sale Orders. It will be pushed to Tally by the sync agent shortly.";
        return RedirectToAction(nameof(PendingParties));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectParty(int id, string? reason)
    {
        var ledger = await _db.Ledgers.FindAsync(id);
        if (ledger == null) return NotFound();
        if (ledger.ApprovalStatus != LedgerApprovalStatus.Pending)
        {
            TempData["Error"] = "This party has already been reviewed.";
            return RedirectToAction(nameof(PendingParties));
        }

        var admin = await _userManager.GetUserAsync(User);
        ledger.ApprovalStatus = LedgerApprovalStatus.Rejected;
        ledger.ReviewedAt = DateTime.Now;
        ledger.ReviewedById = admin?.Id;
        ledger.RejectionReason = reason;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Party \"{ledger.LedgerName}\" rejected.";
        return RedirectToAction(nameof(PendingParties));
    }

    // Role Rights — Admin isn't listed here since it always has full access
    // (PermissionService.HasAsync short-circuits for it); every other role,
    // built-in or Admin-created, gets a column in the matrix.
    [HttpGet]
    public async Task<IActionResult> RoleRights()
    {
        var roles = (await GetAllRoleNamesAsync()).Where(r => r != AppRoles.Admin).ToArray();

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
                // Custom roles with no seeded row default to no access — an
                // Admin creating a brand-new role has to explicitly grant
                // rights here, rather than the role quietly getting
                // everything. Manager/Salesman keep their historical
                // "unseeded == allowed" fallback for compatibility.
                matrix[role][key] = perm?.IsAllowed ?? IsProtectedRole(role);
            }
        }

        return View(new RoleRightsVM { Matrix = matrix, Roles = roles });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RoleRights(IFormCollection form)
    {
        var roles = (await GetAllRoleNamesAsync()).Where(r => r != AppRoles.Admin).ToArray();

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

    // Manage Roles — Admin can create additional roles beyond the 3
    // built-ins; a new role starts with no permissions until granted from
    // Role Rights above (see RoleRights()'s IsProtectedRole fallback).
    public async Task<IActionResult> ManageRoles()
    {
        var roleNames = await GetAllRoleNamesAsync();
        var userCounts = await _db.Users
            .GroupBy(u => u.Role)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync();

        var roles = roleNames.Select(r => new RoleListItemVM
        {
            Name = r,
            IsProtected = IsProtectedRole(r),
            UserCount = userCounts.FirstOrDefault(x => x.Role == r)?.Count ?? 0
        }).ToList();

        return View(roles);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRole(string roleName)
    {
        roleName = (roleName ?? "").Trim();
        if (string.IsNullOrEmpty(roleName))
        {
            TempData["Error"] = "Role name is required.";
            return RedirectToAction(nameof(ManageRoles));
        }
        if (roleName.Length > 50)
        {
            TempData["Error"] = "Role name must be under 50 characters.";
            return RedirectToAction(nameof(ManageRoles));
        }
        if (await _roleManager.RoleExistsAsync(roleName))
        {
            TempData["Error"] = $"A role named \"{roleName}\" already exists.";
            return RedirectToAction(nameof(ManageRoles));
        }

        var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(ManageRoles));
        }

        TempData["Success"] = $"Role \"{roleName}\" created. Set what it can access from Role Rights.";
        return RedirectToAction(nameof(ManageRoles));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(string roleName)
    {
        if (IsProtectedRole(roleName))
        {
            TempData["Error"] = "Built-in roles can't be deleted.";
            return RedirectToAction(nameof(ManageRoles));
        }

        var inUse = await _db.Users.AnyAsync(u => u.Role == roleName);
        if (inUse)
        {
            TempData["Error"] = $"\"{roleName}\" is still assigned to one or more users — reassign them first.";
            return RedirectToAction(nameof(ManageRoles));
        }

        var role = await _roleManager.FindByNameAsync(roleName);
        if (role != null) await _roleManager.DeleteAsync(role);

        var stalePerms = await _db.RolePermissions.Where(p => p.Role == roleName).ToListAsync();
        _db.RolePermissions.RemoveRange(stalePerms);
        await _db.SaveChangesAsync();
        _permSvc.InvalidateCache();

        TempData["Success"] = $"Role \"{roleName}\" deleted.";
        return RedirectToAction(nameof(ManageRoles));
    }

    // User Activity
    //
    // BUG FIXED HERE: the filter parameter used to be named "action" — that
    // collides with ASP.NET Core's own ambient route value of the same name
    // (every MVC action invocation has RouteData.Values["action"] set to the
    // method it routed to, which for this URL is literally "UserActivity").
    // MVC's default value-provider order checks route values before the
    // query string for a plain string parameter, so "action" was ALWAYS
    // binding to "UserActivity" (the route name) instead of the query
    // string's ?action=Login/etc — and since no ActivityActions constant is
    // literally "UserActivity", `a.Action == action` matched zero rows on
    // every single page load, filters or not, regardless of how much real
    // data was in the table. Renamed to activityAction to stop colliding.
    public async Task<IActionResult> UserActivity(string? userId, string? activityAction, DateTime? from, DateTime? to, int page = 1)
    {
        var query = _db.UserActivities.AsQueryable();

        if (!string.IsNullOrEmpty(userId))         query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrEmpty(activityAction)) query = query.Where(a => a.Action == activityAction);
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
        ViewBag.Action   = activityAction;
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
