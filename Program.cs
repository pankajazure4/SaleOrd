using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SaleOrd.BackgroundServices;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

var builder = WebApplication.CreateBuilder(args);

// DB
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    // "Standard" combination: 8+ chars, mixing upper/lower case and a digit.
    // Not requiring a symbol too — keeps it approachable for a signup form
    // while still ruling out plain dictionary words. Enforced everywhere a
    // password is set via UserManager (self-signup, Admin's Create/Edit
    // User) since this is Identity's own global policy, not a one-off check.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

// Tally HTTP client
builder.Services.AddHttpClient<TallyService>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

// Background services
builder.Services.AddHostedService<MasterSyncJob>();
builder.Services.AddHostedService<OrderPushJob>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SaleOrd.Services.ActiveCompanyResolver>();
builder.Services.AddScoped<SaleOrd.Services.PermissionService>();
builder.Services.AddScoped<SaleOrd.Services.UserActivityService>();
builder.Services.AddScoped<SaleOrd.Services.LicenseService>();
builder.Services.AddScoped<SaleOrd.Services.VoucherInventorySyncService>();
builder.Services.AddSingleton<SaleOrd.Services.SyncCoordinator>();
builder.Services.AddMemoryCache();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Migrate & seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SaleOrd.Licensing.LicenseGateMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var db = services.GetRequiredService<AppDbContext>();

    // Seed roles
    foreach (var role in new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Salesman })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Seed default company
    if (!db.Companies.Any())
    {
        db.Companies.Add(new Company
        {
            CompanyName = "Demo Company",
            TallyCompanyName = "Demo Company",
            TallyIp = "localhost",
            TallyPort = 9000,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    // Seed default AppSettings for edit window
    var existingSettingKeys = db.AppSettings.Select(s => s.Key).ToHashSet();
    var defaultSettings = new Dictionary<string, string>
    {
        ["OrderEditEnabled"]    = "true",
        ["OrderEditCutoffHour"] = "19",
        ["TaxLedgerIGST"]      = "IGST",
        ["TaxLedgerCGST"]      = "CGST",
        ["TaxLedgerSGST"]      = "SGST",
        ["DefaultTaxType"]     = "None",
        ["DefaultTaxPercent"]  = "0",
        ["SalesLedger"]        = "Sales",
        ["TaxLedgerRoundOff"]  = "Round Off",
    };
    foreach (var (key, val) in defaultSettings)
    {
        if (!existingSettingKeys.Contains(key))
            db.AppSettings.Add(new AppSetting { Key = key, Value = val });
    }
    if (defaultSettings.Keys.Any(k => !existingSettingKeys.Contains(k)))
        await db.SaveChangesAsync();

    // Seed / upsert role permissions for all defined permission keys
    {
        var existing = db.RolePermissions.ToList();
        var seedRoles = new[]
        {
            (AppRoles.Manager,  AppPermissions.ManagerDefaults),
            (AppRoles.Salesman, AppPermissions.SalesmanDefaults),
        };
        bool changed = false;
        foreach (var (role, defaults) in seedRoles)
        {
            foreach (var (key, _, _) in AppPermissions.All)
            {
                if (!existing.Any(p => p.Role == role && p.PermissionKey == key))
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        Role          = role,
                        PermissionKey = key,
                        IsAllowed     = defaults.Contains(key)
                    });
                    changed = true;
                }
            }
        }
        if (changed) await db.SaveChangesAsync();
    }

    // Seed admin user
    const string adminEmail = "admin@saleord.local";
    if (await userManager.FindByEmailAsync(adminEmail) == null)
    {
        var company = db.Companies.First();
        var admin = new AppUser
        {
            FullName = "Administrator",
            Email = adminEmail,
            UserName = adminEmail,
            CompanyId = company.CompanyId,
            Role = AppRoles.Admin
        };
        var result = await userManager.CreateAsync(admin, "Admin@123");
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, AppRoles.Admin);
            db.UserCompanies.Add(new UserCompany { UserId = admin.Id, CompanyId = company.CompanyId });
            await db.SaveChangesAsync();
        }
    }
}
