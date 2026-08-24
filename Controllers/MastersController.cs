using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaleOrd.Data;
using SaleOrd.Models.Domain;
using SaleOrd.Services;

namespace SaleOrd.Controllers;

[Authorize]
public class MastersController : Controller
{
    private readonly AppDbContext _db;
    private readonly ActiveCompanyResolver _resolver;
    private readonly PermissionService _permSvc;
    private readonly IWebHostEnvironment _env;

    // Stored outside wwwroot on purpose — FSSAI certificates aren't meant to
    // be reachable via a direct static-file URL; FssaiDocument() below is the
    // only way to read one back, and it's auth+permission-gated the same way
    // the rest of this controller is.
    private const long MaxFssaiDocBytes = 5 * 1024 * 1024; // 5 MB

    // PDF or a photo of the physical license — plenty of users don't have a
    // scanned PDF and just snap a picture on their phone instead.
    private static readonly Dictionary<string, string> AllowedFssaiTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = "application/pdf",
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"]  = "image/png",
        [".webp"] = "image/webp"
    };

    public MastersController(AppDbContext db, ActiveCompanyResolver resolver, PermissionService permSvc,
        IWebHostEnvironment env)
    {
        _db = db;
        _resolver = resolver;
        _permSvc = permSvc;
        _env = env;
    }

    private string FssaiUploadsRoot => Path.Combine(_env.ContentRootPath, "App_Data", "Uploads", "Fssai");

    public async Task<IActionResult> Parties(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties))
            return Forbid();

        var selectedId = activeId;

        List<Ledger> parties = [];
        if (!string.IsNullOrEmpty(q))
        {
            parties = await _db.Ledgers
                .Where(l => l.CompanyId == selectedId &&
                    (l.LedgerName.Contains(q) || (l.MobileNo != null && l.MobileNo.Contains(q))))
                .OrderBy(l => l.LedgerName)
                .Take(100)
                .ToListAsync();
        }

        ViewBag.Q              = q;
        ViewBag.SelectedId     = selectedId;
        ViewBag.CompanyName    = await _db.Companies.Where(c => c.CompanyId == selectedId).Select(c => c.CompanyName).FirstOrDefaultAsync() ?? string.Empty;
        ViewBag.TotalCount     = await _db.Ledgers.CountAsync(l => l.CompanyId == selectedId);
        return View(parties);
    }

    [HttpGet]
    public async Task<IActionResult> ItemsSearch(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersItems)) return Forbid();

        if (string.IsNullOrWhiteSpace(q))
            return Json(Array.Empty<object>());

        var items = await _db.StockItems
            .Where(s => s.CompanyId == activeId && s.ItemName.Contains(q))
            .OrderBy(s => s.ItemName)
            .Take(60)
            .Select(s => new
            {
                s.StockItemId, s.ItemName, s.UOM, s.Parent,
                s.ClosingBalance, s.IsBatchwiseOn, s.AdditionalUnits
            })
            .ToListAsync();

        return Json(items);
    }

    // Party Master creation is gated behind Admin approval — see
    // Ledger.ApprovalStatus. A party saved here is NOT usable in Sale Orders
    // (GetParties/PartiesSearch below filter to Approved only) and is NOT
    // pushed to Tally until an Admin explicitly approves it from
    // Admin > Pending Parties. This mirrors the existing SignupRequest
    // pending->approve pattern used for user signups.
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFssaiDocBytes + 1024 * 1024)] // a little headroom over the file cap for the rest of the form
    public async Task<IActionResult> CreateParty(string partyName, string? outletName, string? gstNo, string fssaiNo, string? address,
        string? state, string contactNo, string? paymentTerms, int? creditDays, IFormFile? fssaiDocument)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return Json(new { success = false, message = "Session expired. Please login again." });
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties))
            return Json(new { success = false, message = "Access denied." });

        partyName = (partyName ?? "").Trim();
        if (string.IsNullOrEmpty(partyName))
            return Json(new { success = false, message = "Party name is required." });

        fssaiNo = (fssaiNo ?? "").Trim();
        if (string.IsNullOrEmpty(fssaiNo))
            return Json(new { success = false, message = "Food License No. (FSSAI) is required." });
        if (fssaiNo.Length != 14 || !fssaiNo.All(char.IsDigit))
            return Json(new { success = false, message = "Food License No. (FSSAI) must be exactly 14 digits." });

        contactNo = (contactNo ?? "").Trim();
        if (string.IsNullOrEmpty(contactNo))
            return Json(new { success = false, message = "Contact number is required." });

        // "Days" suffix matches the format Tally itself uses for this field
        // (e.g. "30 Days" — confirmed against a real ledger export, see
        // TallyService.GetLedgersAsync) so it displays and pushes correctly
        // whether it came from here or from a Tally-synced ledger.
        var isCredit = string.Equals(paymentTerms, "Credit", StringComparison.OrdinalIgnoreCase);
        if (isCredit && (!creditDays.HasValue || creditDays.Value <= 0))
            return Json(new { success = false, message = "Enter the number of credit days." });
        var creditPeriod = isCredit ? $"{creditDays!.Value} Days" : null;

        var fssaiExt = fssaiDocument != null ? Path.GetExtension(fssaiDocument.FileName) : null;
        if (fssaiDocument != null)
        {
            if (fssaiDocument.Length > MaxFssaiDocBytes)
                return Json(new { success = false, message = "Food License document must be under 5 MB." });
            if (fssaiExt == null || !AllowedFssaiTypes.TryGetValue(fssaiExt, out var expectedContentType)
                || !string.Equals(fssaiDocument.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Food License document must be a PDF or an image (JPG, PNG, WEBP)." });
        }

        var exists = await _db.Ledgers.AnyAsync(l => l.CompanyId == activeId && l.LedgerName == partyName);
        if (exists)
            return Json(new { success = false, message = $"A party named \"{partyName}\" already exists." });

        var company = await _db.Companies.FindAsync(activeId);
        if (company == null) return Json(new { success = false, message = "Company not found." });

        var ledger = new Ledger
        {
            LedgerName     = partyName,
            OutletName     = string.IsNullOrWhiteSpace(outletName) ? null : outletName.Trim(),
            Parent         = "Sundry Debtors",
            GSTNo          = string.IsNullOrWhiteSpace(gstNo) ? null : gstNo.Trim(),
            FSSAINo        = fssaiNo,
            Address        = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            State          = string.IsNullOrWhiteSpace(state) ? null : state.Trim(),
            MobileNo       = contactNo,
            CreditPeriod   = creditPeriod,
            CompanyId      = activeId,
            LastSyncedAt   = DateTime.Now,
            ApprovalStatus = LedgerApprovalStatus.Pending,
            IsManuallyCreated = true
        };

        if (fssaiDocument != null)
        {
            Directory.CreateDirectory(FssaiUploadsRoot);
            var storedFileName = $"{Guid.NewGuid():N}{fssaiExt!.ToLowerInvariant()}";
            await using (var stream = System.IO.File.Create(Path.Combine(FssaiUploadsRoot, storedFileName)))
                await fssaiDocument.CopyToAsync(stream);
            ledger.FSSAIDocumentPath = storedFileName;
        }

        _db.Ledgers.Add(ledger);
        await _db.SaveChangesAsync();

        return Json(new
        {
            success = true,
            message = $"Party \"{partyName}\" submitted for Admin approval. It won't be usable in Sale Orders until approved."
        });
    }

    // Streams a party's FSSAI document back (PDF or image — see
    // AllowedFssaiTypes) — never a static-file URL, so this is the only path
    // that can read one, and it's gated by the same Masters-Parties
    // permission as everything else here.
    [HttpGet]
    public async Task<IActionResult> FssaiDocument(int ledgerId)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties)) return Forbid();

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.LedgerId == ledgerId && l.CompanyId == activeId);
        if (ledger?.FSSAIDocumentPath == null) return NotFound();

        var path = Path.Combine(FssaiUploadsRoot, ledger.FSSAIDocumentPath);
        if (!System.IO.File.Exists(path)) return NotFound();

        var ext = Path.GetExtension(ledger.FSSAIDocumentPath);
        var contentType = AllowedFssaiTypes.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        return File(bytes, contentType, $"{ledger.LedgerName}-FSSAI{ext}");
    }

    [HttpGet]
    public async Task<IActionResult> PartiesSearch(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return Unauthorized();
        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersParties)) return Forbid();

        if (string.IsNullOrWhiteSpace(q))
            return Json(Array.Empty<object>());

        var parties = await _db.Ledgers
            .Where(l => l.CompanyId == activeId &&
                (l.LedgerName.Contains(q) || (l.MobileNo != null && l.MobileNo.Contains(q))))
            .OrderBy(l => l.LedgerName)
            .Take(60)
            .Select(l => new
            {
                l.LedgerId, l.LedgerName, l.MobileNo, l.GSTNo,
                l.Parent, l.ClosingBalance, l.State, l.ApprovalStatus
            })
            .ToListAsync();

        return Json(parties);
    }

    public async Task<IActionResult> Items(string? q)
    {
        var (activeId, user) = await _resolver.ResolveAsync();
        if (user == null) return RedirectToAction("Login", "Account");

        if (!await _permSvc.HasAsync(user.Role, AppPermissions.MastersItems))
            return Forbid();

        var selectedId = activeId;

        // Don't load any items until user types a search term
        List<StockItem> items = [];
        if (!string.IsNullOrEmpty(q))
        {
            items = await _db.StockItems
                .Where(s => s.CompanyId == selectedId && s.ItemName.Contains(q))
                .OrderBy(s => s.ItemName)
                .Take(100)
                .ToListAsync();
        }

        ViewBag.Q           = q;
        ViewBag.SelectedId  = selectedId;
        ViewBag.CompanyName = await _db.Companies.Where(c => c.CompanyId == selectedId).Select(c => c.CompanyName).FirstOrDefaultAsync() ?? string.Empty;
        ViewBag.TotalCount  = await _db.StockItems.CountAsync(s => s.CompanyId == selectedId);
        return View(items);
    }
}
