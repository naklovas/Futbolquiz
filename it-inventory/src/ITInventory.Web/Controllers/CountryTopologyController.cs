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

    /// <summary>
    /// Checks the file's actual bytes against its claimed extension so a renamed file (e.g.
    /// something.exe saved as diagram.png) can't slip past the extension allow-list above.
    /// .vsdx is a ZIP/OOXML package (PK signature, same as .docx/.xlsx); .vsd is a legacy OLE
    /// compound file; .drawio is plain XML/text, so it's checked for a leading '&lt;' instead of
    /// a fixed binary signature.
    /// </summary>
    private static bool ContentMatchesExtension(string extension, byte[] data)
    {
        bool StartsWith(params byte[] signature) =>
            data.Length >= signature.Length && signature.AsSpan().SequenceEqual(data.AsSpan(0, signature.Length));

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => StartsWith((byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-'),
            ".png" => StartsWith(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".jpg" or ".jpeg" => StartsWith(0xFF, 0xD8, 0xFF),
            ".vsdx" => StartsWith(0x50, 0x4B, 0x03, 0x04),
            ".vsd" => StartsWith(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1),
            ".drawio" => data.Length > 0
                && System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 512)).TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith('<'),
            _ => false
        };
    }

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
        var bytes = stream.ToArray();

        if (!ContentMatchesExtension(extension, bytes))
        {
            TempData["Error"] = "The file's contents don't match its extension. Make sure it's a genuine PDF, Visio, draw.io or image file.";
            return RedirectToAction(nameof(Index), new { countryId });
        }

        var existing = await _db.CountryTopologyFiles.FirstOrDefaultAsync(f => f.CountryId == countryId);
        if (existing is null)
        {
            existing = new CountryTopologyFile { CountryId = countryId };
            _db.CountryTopologyFiles.Add(existing);
        }

        existing.FileName = file.FileName;
        existing.ContentType = contentType;
        existing.FileData = bytes;
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

        // fileDownloadName forces Content-Disposition: attachment -- always a plain save,
        // never rendered by the browser, regardless of content type.
        return File(file.FileData, file.ContentType, file.FileName);
    }

    private static readonly HashSet<string> InlinePreviewableContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg"
    };

    /// <summary>
    /// Separate from Download on purpose: this renders inline (no fileDownloadName, so no
    /// forced attachment disposition) instead of prompting a save. Only ever serves PDF/PNG/
    /// JPEG -- both browsers' native PDF viewer and &lt;img&gt; rendering are safe sandboxed
    /// contexts that can't execute script from the file's content, unlike e.g. serving an XML
    /// (.drawio) file inline. Visio/draw.io have no safe/native in-browser viewer anyway, so
    /// they're not offered a preview at all (and .drawio specifically is never sent to an
    /// external viewer service -- that would leak the diagram off-premises).
    /// </summary>
    public async Task<IActionResult> Preview(int countryId)
    {
        if (!_currentUser.IsAdmin && countryId != _currentUser.CountryId) return Forbid();

        var file = await _db.CountryTopologyFiles.FirstOrDefaultAsync(f => f.CountryId == countryId);
        if (file is null || !InlinePreviewableContentTypes.Contains(file.ContentType)) return NotFound();

        return File(file.FileData, file.ContentType);
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
