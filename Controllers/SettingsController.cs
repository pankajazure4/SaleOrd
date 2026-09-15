using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Licensing;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize(Roles = "Admin")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly TallyService _tally;
    private readonly IConfiguration _config;
    private readonly LicenseService _license;
    private readonly IWebHostEnvironment _env;

    // The app logo is meant to be publicly visible (topbar, login screen), so
    // unlike FSSAI docs it lives under wwwroot and is served as a normal
    // static file rather than through a gated controller action.
    private const long MaxLogoBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly string[] AllowedLogoExtensions = { ".png", ".jpg", ".jpeg", ".svg" };
    private string LogoUploadsRoot => Path.Combine(_env.WebRootPath, "uploads", "branding");

    public SettingsController(AppDbContext db, TallyService tally, IConfiguration config, LicenseService license,
        IWebHostEnvironment env)
    {
        _db = db;
        _tally = tally;
        _config = config;
        _license = license;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        // One round-trip for every AppSettings row instead of a separate
        // query just for TallyUrl plus another for everything else.
        var allSettings = await _db.AppSettings.ToListAsync();
        var saved = allSettings.FirstOrDefault(s => s.Key == "TallyUrl")?.Value;
        var defaultUrl = $"http://{_config["TallySettings:DefaultIp"] ?? "localhost"}:{_config.GetValue<int>("TallySettings:DefaultPort", 9000)}";
        var tallyUrl   = saved ?? defaultUrl;
        var companies  = await _db.Companies
            .Where(c => c.IsActive)
            .OrderBy(c => c.CompanyName)
            .ToListAsync();

        // Detecting which company is currently open in Tally used to happen
        // here too, blocking every single page load on a live network call
        // to the client's Tally instance (up to 5s, every time, whether or
        // not anyone needed it). It's pure redundancy: the page's own "Check
        // Status" button (checkStatus() in the view) already does the exact
        // same detection over AJAX and sets this same dropdown — so it now
        // runs there instead, auto-fired once on page load (still automatic,
        // just no longer blocking the page itself from rendering first).
        int selectedCompanyId = companies.FirstOrDefault()?.CompanyId ?? 0;

        ViewBag.TallyUrl          = tallyUrl;
        ViewBag.DefaultUrl        = defaultUrl;
        ViewBag.Companies         = companies;
        ViewBag.SelectedCompanyId = selectedCompanyId;

        // Load tax defaults for display
        ViewBag.DefaultTaxType     = allSettings.FirstOrDefault(s => s.Key == "DefaultTaxType")?.Value ?? "None";
        ViewBag.TaxLedgerIGST     = allSettings.FirstOrDefault(s => s.Key == "TaxLedgerIGST")?.Value ?? "IGST";
        ViewBag.TaxLedgerCGST     = allSettings.FirstOrDefault(s => s.Key == "TaxLedgerCGST")?.Value ?? "CGST";
        ViewBag.TaxLedgerSGST     = allSettings.FirstOrDefault(s => s.Key == "TaxLedgerSGST")?.Value ?? "SGST";
        ViewBag.SalesLedger       = allSettings.FirstOrDefault(s => s.Key == "SalesLedger")?.Value ?? "Sales";
        ViewBag.TaxLedgerRoundOff = allSettings.FirstOrDefault(s => s.Key == "TaxLedgerRoundOff")?.Value ?? "Round Off";

        // Order defaults (voucher type; Godown/Batch used to be admin-editable
        // here too, but a stale or mistyped value silently attached wrong
        // Godown/Batch data to every pushed order — a fixed "Primary Batch"
        // (Tally's own real, pre-existing default batch) is used instead
        // now, unconditionally, so there's nothing left to configure or get
        // wrong. See TallyService.PushSaleOrderAsync.)
        ViewBag.DefaultVoucherType = allSettings.FirstOrDefault(s => s.Key == "DefaultVoucherType")?.Value ?? "Sales Order";
        ViewBag.TallyFssaiUdfField = allSettings.FirstOrDefault(s => s.Key == "TallyFssaiUdfField")?.Value ?? "";
        ViewBag.TallyZoneUdfField  = allSettings.FirstOrDefault(s => s.Key == "TallyZoneUdfField")?.Value ?? "";

        // License status
        var licenseKey = await _license.GetLicenseKeyAsync();
        ViewBag.LicenseMaskedKey = LicenseKeyProtector.Mask(licenseKey);
        ViewBag.LicenseHasKey    = !string.IsNullOrEmpty(licenseKey);
        ViewBag.LicenseStatus    = await _license.CheckAsync();

        ViewBag.LogoUrl = allSettings.FirstOrDefault(s => s.Key == "LogoUrl")?.Value;

        ViewBag.Zones = await _db.Zones
            .Include(z => z.Company)
            .OrderBy(z => z.Company!.CompanyName).ThenBy(z => z.ZoneName)
            .ToListAsync();

        return View();
    }

    // Zones — the fixed list an Admin picks a party's Zone from at Party
    // Approval (Admin > Pending Parties). Per-company; no delete, just
    // deactivate (ToggleZone) — same convention as ToggleUser, so a Zone
    // already assigned to existing parties never becomes a dangling
    // reference, it just stops being offered for new approvals.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddZone(string zoneName, int companyId)
    {
        if (string.IsNullOrWhiteSpace(zoneName) || companyId <= 0)
        {
            TempData["Error"] = "Enter a zone name and pick a company.";
            return RedirectToAction(nameof(Index));
        }
        var name = zoneName.Trim();
        if (await _db.Zones.AnyAsync(z => z.CompanyId == companyId && z.ZoneName == name))
        {
            TempData["Error"] = $"Zone \"{name}\" already exists for this company.";
            return RedirectToAction(nameof(Index));
        }
        _db.Zones.Add(new Zone { ZoneName = name, CompanyId = companyId, IsActive = true });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Zone \"{name}\" added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleZone(int id)
    {
        var zone = await _db.Zones.FindAsync(id);
        if (zone == null) return NotFound();
        zone.IsActive = !zone.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxLogoBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile logo)
    {
        if (logo == null || logo.Length == 0)
        {
            TempData["Error"] = "Choose an image file first.";
            return RedirectToAction(nameof(Index));
        }
        if (logo.Length > MaxLogoBytes)
        {
            TempData["Error"] = "Logo image must be under 2 MB.";
            return RedirectToAction(nameof(Index));
        }
        var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
        if (!AllowedLogoExtensions.Contains(ext))
        {
            TempData["Error"] = "Logo must be a PNG, JPG or SVG image.";
            return RedirectToAction(nameof(Index));
        }

        Directory.CreateDirectory(LogoUploadsRoot);

        // Clear out any previously uploaded logo (possibly a different
        // extension) so old files don't pile up under wwwroot.
        foreach (var oldExt in AllowedLogoExtensions)
        {
            var oldPath = Path.Combine(LogoUploadsRoot, $"logo{oldExt}");
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }

        var fileName = $"logo{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(LogoUploadsRoot, fileName)))
            await logo.CopyToAsync(stream);

        // Cache-bust with a version token so browsers pick up the new image
        // immediately instead of serving a stale cached one at the same URL.
        var logoUrl = $"/uploads/branding/{fileName}?v={DateTime.UtcNow.Ticks}";

        var setting = await _db.AppSettings.FindAsync("LogoUrl");
        if (setting == null) _db.AppSettings.Add(new AppSetting { Key = "LogoUrl", Value = logoUrl });
        else setting.Value = logoUrl;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Logo updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLogo()
    {
        var setting = await _db.AppSettings.FindAsync("LogoUrl");
        if (setting != null)
        {
            _db.AppSettings.Remove(setting);
            await _db.SaveChangesAsync();
        }
        foreach (var ext in AllowedLogoExtensions)
        {
            var path = Path.Combine(LogoUploadsRoot, $"logo{ext}");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }

        TempData["Success"] = "Logo removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTaxSettings(string defaultTaxType,
        string taxLedgerIGST, string taxLedgerCGST, string taxLedgerSGST,
        string salesLedger, string taxLedgerRoundOff)
    {
        var keys = new Dictionary<string, string>
        {
            // The rate itself now always comes from each item's own
            // StockItemTaxSlabs (synced from Tally) — DefaultTaxPercent is
            // no longer read anywhere; only which duty heads apply
            // (IGST vs CGST+SGST) is still a setting.
            ["DefaultTaxType"]    = defaultTaxType ?? "None",
            ["TaxLedgerIGST"]    = taxLedgerIGST ?? "IGST",
            ["TaxLedgerCGST"]    = taxLedgerCGST ?? "CGST",
            ["TaxLedgerSGST"]    = taxLedgerSGST ?? "SGST",
            ["SalesLedger"]      = salesLedger ?? "Sales",
            ["TaxLedgerRoundOff"]= taxLedgerRoundOff ?? "Round Off",
        };
        foreach (var (key, val) in keys)
        {
            var s = await _db.AppSettings.FindAsync(key);
            if (s == null) _db.AppSettings.Add(new AppSetting { Key = key, Value = val });
            else s.Value = val;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Tax settings saved.";
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveOrderDefaults(string defaultVoucherType, string? tallyFssaiUdfField, string? tallyZoneUdfField)
    {
        // DefaultGodownName/DefaultBatchName intentionally no longer read
        // here — see the comment above ViewBag.DefaultVoucherType in Index().
        var keys = new Dictionary<string, string>
        {
            ["DefaultVoucherType"] = string.IsNullOrWhiteSpace(defaultVoucherType) ? "Sales Order" : defaultVoucherType.Trim(),
            ["TallyFssaiUdfField"] = tallyFssaiUdfField?.Trim() ?? "",
            ["TallyZoneUdfField"]  = tallyZoneUdfField?.Trim() ?? "",
        };
        foreach (var (key, val) in keys)
        {
            var s = await _db.AppSettings.FindAsync(key);
            if (s == null) _db.AppSettings.Add(new AppSetting { Key = key, Value = val });
            else s.Value = val;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Order defaults saved.";
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTallyUrl(string tallyUrl)
    {
        tallyUrl = tallyUrl.Trim();
        var setting = await _db.AppSettings.FindAsync("TallyUrl");
        if (setting == null)
            _db.AppSettings.Add(new AppSetting { Key = "TallyUrl", Value = tallyUrl });
        else
            setting.Value = tallyUrl;

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "Saved" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLicense(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return Json(new { success = false, message = "Enter a license key." });

        await _license.SaveLicenseKeyAsync(licenseKey);
        var result = await _license.CheckAsync();

        return Json(new
        {
            success = result.IsValid,
            message = result.Message,
            expiresOn = result.ExpiresOn,
            isTrial = result.IsTrial,
            maskedKey = LicenseKeyProtector.Mask(await _license.GetLicenseKeyAsync())
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckLicense()
    {
        _license.Invalidate();
        var result = await _license.CheckAsync();

        return Json(new
        {
            success = result.IsValid,
            message = result.Message,
            expiresOn = result.ExpiresOn,
            isTrial = result.IsTrial
        });
    }

    [HttpGet]
    public async Task<IActionResult> RawXml(string? companyName)
    {
        var url = await _db.AppSettings.Where(s => s.Key == "TallyUrl").Select(s => s.Value).FirstOrDefaultAsync()
                  ?? $"http://{_config["TallySettings:DefaultIp"] ?? "localhost"}:{_config.GetValue<int>("TallySettings:DefaultPort", 9000)}";
        var raw = await _tally.GetRawXmlAsync(url, companyName ?? string.Empty);
        return Content(raw, "application/xml");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckStatus(string tallyUrl)
    {
        var url = tallyUrl?.Trim();
        if (string.IsNullOrEmpty(url))
            url = await _db.AppSettings.Where(s => s.Key == "TallyUrl").Select(s => s.Value).FirstOrDefaultAsync()
                  ?? $"http://{_config["TallySettings:DefaultIp"] ?? "localhost"}:{_config.GetValue<int>("TallySettings:DefaultPort", 9000)}";

        var (ok, companies) = await _tally.CheckStatusAsync(url);
        var status = ok ? (companies.Any() ? "online" : "warning") : "offline";

        // Match open Tally companies to our registered companies by TallyCompanyName
        int matchedCompanyId = 0;
        string matchedCompanyName = "";
        if (ok && companies.Any())
        {
            var match = await _db.Companies
                .Where(c => c.IsActive && companies.Contains(c.TallyCompanyName))
                .Select(c => new { c.CompanyId, c.CompanyName })
                .FirstOrDefaultAsync();
            if (match != null)
            {
                matchedCompanyId   = match.CompanyId;
                matchedCompanyName = match.CompanyName;
            }
        }

        return Json(new
        {
            reachable = ok,
            status,
            companies,
            matchedCompanyId,
            matchedCompanyName,
            message = ok
                ? (companies.Any()
                    ? $"Connected. Open in Tally: {string.Join(", ", companies)}"
                    : "Connected, but no company is open in Tally.")
                : "Tally is not reachable. Check the URL and make sure Tally is running."
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FlushCompanyData(int companyId)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.IsActive);
        if (company == null)
            return NotFound();

        await using var tx = await _db.Database.BeginTransactionAsync();

        var orderIds = await _db.SaleOrders
            .Where(o => o.CompanyId == companyId)
            .Select(o => o.SaleOrderId)
            .ToListAsync();

        if (orderIds.Any())
        {
            await _db.SaleOrderItems
                .Where(i => orderIds.Contains(i.SaleOrderId))
                .ExecuteDeleteAsync();
        }

        await _db.SaleOrders.Where(o => o.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.Ledgers.Where(l => l.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.StockItems.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.Godowns.Where(g => g.CompanyId == companyId).ExecuteDeleteAsync();
        await _db.SyncLogs.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync();

        company.LastMasterSyncAt = null;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["Success"] = $"Deleted synced data for {company.CompanyName}.";
        return RedirectToAction(nameof(Index));
    }
}
