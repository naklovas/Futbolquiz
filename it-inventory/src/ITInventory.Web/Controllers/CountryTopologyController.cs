using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

/// <summary>
/// One logical network topology diagram per country. Any country data-entry user (CanEdit) can
/// upload/replace/delete their own country's diagram; admins can do so for any country. Viewing
/// is available to anyone who can already see that country's data.
/// </summary>
public class CountryTopologyController : Controller
{
    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".vsdx"] = "application/vnd.ms-visio.drawing",
        [".vsd"] = "application/vnd.visio",
        [".drawio"] = "application/xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg"
    };

    private const long MaxFileBytes = 20 * 1024 * 1024; // 20 MB

    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public CountryTopologyController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _activityLogger = activityLogger;
    }

    public async Task<IActionResult> Index(int? countryId)
    {
        var isAdmin = _currentUser.IsAdmin;
        var effectiveCountryId = isAdmin ? countryId : _currentUser.CountryId;

        if (isAdmin)
        {
            var countries = await _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
                .Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();
            ViewBag.CountryOptions = new SelectList(countries, "Id", "Label", effectiveCountryId);
        }

        ViewBag.IsAdmin = isAdmin;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.SelectedCountryId = effectiveCountryId;

        if (!effectiveCountryId.HasValue)
        {
            return View((CountryTopologyFile?)null);
        }

        var country = await _db.Countries.FindAsync(effectiveCountryId.Value);
        ViewBag.CountryLabel = country?.DisplayName ?? country?.Name;

        var file = await _db.CountryTopologyFiles.FirstOrDefaultAsync(f => f.CountryId == effectiveCountryId.Value);
        return View(file);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(int countryId, IFormFile file)
    {
        if (!_currentUser.CanEdit) return Forbid();
        if (!_currentUser.IsAdmin && countryId != _currentUser.CountryId) return Forbid();

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please choose a file.";
            return RedirectToAction(nameof(Index), new { countryId });
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.TryGetValue(extension, out var contentType))
        {
            TempData["Error"] = "Only PDF, Visio (.vsdx/.vsd), draw.io (.drawio) or image (.png/.jpg) files are supported.";
            return RedirectToAction(nameof(Index), new { countryId });
        }

        if (file.Length > MaxFileBytes)
        {
            TempData["Error"] = "File is too large (max 20 MB).";
            return RedirectToAction(nameof(Index), new { countryId });
        }

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);

        var existing = await _db.CountryTopologyFiles.FirstOrDefaultAsync(f => f.CountryId == countryId);
        if (existing is null)
        {
            existing = new CountryTopologyFile { CountryId = countryId };
            _db.CountryTopologyFiles.Add(existing);
        }

        existing.FileName = file.FileName;
        existing.ContentType = contentType;
        existing.FileData = stream.ToArray();
        existing.FileSize = file.Length;
        existing.UploadedAt = DateTime.UtcNow;
        existing.UploadedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Upload", "CountryTopologyFile", details: file.FileName);

        TempData["Success"] = "Topology file uploaded.";
        return RedirectToAction(nameof(Index), new { countryId });
    }

    public async Task<IActionResult> Download(int countryId)
    {
        if (!_currentUser.IsAdmin && countryId != _currentUser.CountryId) return Forbid();

        var file = await _db.CountryTopologyFiles.FirstOrDefaultAsync(f => f.CountryId == countryId);
        if (file is null) return NotFound();

        return File(file.FileData, file.ContentType, file.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int countryId)
    {
        if (!_currentUser.CanEdit) return Forbid();
        if (!_currentUser.IsAdmin && countryId != _currentUser.CountryId) return Forbid();

        var file = await _db.CountryTopologyFiles.FirstOrDefaultAsync(f => f.CountryId == countryId);
        if (file is not null)
        {
            _db.CountryTopologyFiles.Remove(file);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Delete", "CountryTopologyFile", details: file.FileName);
        }

        return RedirectToAction(nameof(Index), new { countryId });
    }
}
