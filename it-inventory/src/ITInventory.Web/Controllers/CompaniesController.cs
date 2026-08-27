using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Common;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Companies;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class CompaniesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public CompaniesController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _activityLogger = activityLogger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? countryId, int page = 1)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        var viewAll = isAdmin && countryId == "all";
        int? selectedCountryId = !viewAll && int.TryParse(countryId, out var parsedCountryId) ? parsedCountryId : null;
        var requiresSelection = isAdmin && !viewAll && !selectedCountryId.HasValue;

        PagedResult<Company> items;
        if (requiresSelection)
        {
            items = new PagedResult<Company> { Items = Array.Empty<Company>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 };
        }
        else
        {
            var query = _db.Companies.Include(c => c.Country).Include(c => c.OriginCountry).Include(c => c.Contacts).AsQueryable();

            if (!isAdmin)
                query = query.Where(c => c.CountryId == scopedCountryId);
            else if (selectedCountryId.HasValue)
                query = query.Where(c => c.CountryId == selectedCountryId.Value);

            items = await query.OrderBy(c => c.Country!.Name).ThenBy(c => c.Name).ToPagedResultAsync(page);
        }

        ViewBag.Countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCountryId = selectedCountryId;
        ViewBag.ViewAll = viewAll;
        ViewBag.RequiresSelection = requiresSelection;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.IsAdmin = isAdmin;

        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? countryId)
    {
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();

        var viewAll = countryId == "all";
        int? selectedCountryId = !viewAll && int.TryParse(countryId, out var parsedCountryId) ? parsedCountryId : null;
        if (!viewAll && !selectedCountryId.HasValue) return BadRequest("Please select a country (or All Countries) first.");

        var query = _db.Companies.Include(c => c.Country).Include(c => c.OriginCountry).Include(c => c.Contacts).AsQueryable();
        if (selectedCountryId.HasValue)
            query = query.Where(c => c.CountryId == selectedCountryId.Value);

        var items = await query.OrderBy(c => c.Country!.Name).ThenBy(c => c.Name).ToListAsync();

        var headers = new[] { "Country", "Company Name", "Type", "Country of Origin", "Contacts", "Active" };
        var rows = items.Select(c => new object?[]
        {
            c.Country?.DisplayName ?? c.Country?.Name,
            c.Name,
            c.CompanyType == CompanyType.Other ? $"Other ({c.OtherTypeDescription})" : c.CompanyType.ToString(),
            c.OriginCountry?.Name,
            string.Join(", ", c.Contacts.Select(x => x.PersonName ?? x.Title ?? x.Email ?? "(contact)")),
            c.IsActive ? "Yes" : "No"
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Companies", headers, rows);
        await _activityLogger.LogAsync("Export", "Company", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Companies_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var vm = new CompanyFormViewModel
        {
            CountryId = isAdmin ? 0 : scopedCountryId ?? 0
        };
        vm.Contacts.Add(new CompanyContactFormViewModel());

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CompanyFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        // Every foreign key below arrives as a plain number from a form field. Checking that the
        // number exists is not enough: the id written into the new row has to COME FROM the row
        // the authorized lookup returned, never from the request. Otherwise the posted value
        // still travels straight into the INSERT and only an existence test stands in its way.
        var requestedCountryId = isAdmin ? (vm.CountryId ?? 0) : (scopedCountryId ?? 0);
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == requestedCountryId
            && (isAdmin || c.Id == scopedCountryId));
        if (country is null)
        {
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");
            await PopulateDropdowns();
            return View(vm);
        }
        vm.CountryId = country.Id;

        if (await _db.Companies.AnyAsync(c => c.Name == vm.Name && c.CountryId == country.Id))
            ModelState.AddModelError(nameof(vm.Name), "A company with this name already exists for this country.");

        if (vm.CompanyType == CompanyType.Other && string.IsNullOrWhiteSpace(vm.OtherTypeDescription))
            ModelState.AddModelError(nameof(vm.OtherTypeDescription), "Please specify the type.");

        OriginCountry? originCountry = null;
        if (vm.OriginCountryId.HasValue)
        {
            originCountry = await _db.OriginCountries.FirstOrDefaultAsync(o => o.Id == vm.OriginCountryId.Value && o.IsActive);
            if (originCountry is null)
            {
                ModelState.AddModelError(nameof(vm.OriginCountryId), "Please select a valid country of origin.");
                await PopulateDropdowns();
                return View(vm);
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new Company
        {
            CountryId = country.Id,
            Name = vm.Name,
            CompanyType = vm.CompanyType,
            OtherTypeDescription = vm.CompanyType == CompanyType.Other ? vm.OtherTypeDescription : null,
            OriginCountryId = originCountry?.Id,
            IsActive = vm.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        foreach (var contact in vm.Contacts.Where(HasAnyValue))
        {
            entity.Contacts.Add(new CompanyContact
            {
                PersonName = contact.PersonName,
                Title = contact.Title,
                Phone = contact.Phone,
                Email = contact.Email
            });
        }

        _db.Companies.Add(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "Company", entity.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var company = await _db.Companies.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id
            && (isAdmin || c.CountryId == scopedCountryId));
        if (company is null) return NotFound();

        var vm = new CompanyFormViewModel
        {
            Id = company.Id,
            CountryId = company.CountryId,
            Name = company.Name,
            CompanyType = company.CompanyType,
            OtherTypeDescription = company.OtherTypeDescription,
            OriginCountryId = company.OriginCountryId,
            IsActive = company.IsActive,
            Contacts = company.Contacts.Select(c => new CompanyContactFormViewModel
            {
                Id = c.Id,
                PersonName = c.PersonName,
                Title = c.Title,
                Phone = c.Phone,
                Email = c.Email
            }).ToList()
        };

        if (vm.Contacts.Count == 0)
            vm.Contacts.Add(new CompanyContactFormViewModel());

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CompanyFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.Companies.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id
            && (isAdmin || c.CountryId == scopedCountryId));
        if (entity is null) return NotFound();

        // Every foreign key below arrives as a plain number from a form field. Checking that the
        // number exists is not enough: the ids written onto the row have to COME FROM the rows
        // the authorized lookups returned, never from the request. Otherwise the posted values
        // still travel straight into the UPDATE with only an existence test in the way.
        var requestedCountryId = isAdmin ? (vm.CountryId ?? 0) : (scopedCountryId ?? 0);
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == requestedCountryId
            && (isAdmin || c.Id == scopedCountryId));
        if (country is null)
        {
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");
            await PopulateDropdowns();
            return View(vm);
        }
        vm.CountryId = country.Id;

        if (await _db.Companies.AnyAsync(c => c.Name == vm.Name && c.CountryId == country.Id && c.Id != id))
            ModelState.AddModelError(nameof(vm.Name), "A company with this name already exists for this country.");

        if (vm.CompanyType == CompanyType.Other && string.IsNullOrWhiteSpace(vm.OtherTypeDescription))
            ModelState.AddModelError(nameof(vm.OtherTypeDescription), "Please specify the type.");

        OriginCountry? originCountry = null;
        if (vm.OriginCountryId.HasValue)
        {
            originCountry = await _db.OriginCountries.FirstOrDefaultAsync(o => o.Id == vm.OriginCountryId.Value && o.IsActive);
            if (originCountry is null)
            {
                ModelState.AddModelError(nameof(vm.OriginCountryId), "Please select a valid country of origin.");
                await PopulateDropdowns();
                return View(vm);
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        entity.CountryId = country.Id;
        entity.Name = vm.Name;
        entity.CompanyType = vm.CompanyType;
        entity.OtherTypeDescription = vm.CompanyType == CompanyType.Other ? vm.OtherTypeDescription : null;
        entity.OriginCountryId = originCountry?.Id;
        entity.IsActive = vm.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        var postedContacts = vm.Contacts.Where(HasAnyValue).ToList();
        var postedIds = postedContacts.Where(c => c.Id != 0).Select(c => c.Id).ToHashSet();

        foreach (var existing in entity.Contacts.Where(c => !postedIds.Contains(c.Id)).ToList())
            _db.CompanyContacts.Remove(existing);

        foreach (var contact in postedContacts)
        {
            if (contact.Id != 0)
            {
                var existing = entity.Contacts.FirstOrDefault(c => c.Id == contact.Id);
                if (existing is not null)
                {
                    existing.PersonName = contact.PersonName;
                    existing.Title = contact.Title;
                    existing.Phone = contact.Phone;
                    existing.Email = contact.Email;
                }
            }
            else
            {
                entity.Contacts.Add(new CompanyContact
                {
                    PersonName = contact.PersonName,
                    Title = contact.Title,
                    Phone = contact.Phone,
                    Email = contact.Email
                });
            }
        }

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "Company", entity.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id
            && (isAdmin || c.CountryId == scopedCountryId));
        if (company is null) return NotFound();

        var companyName = company.Name;
        _db.Companies.Remove(company);

        try
        {
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Delete", "Company", companyName);
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Could not delete because licenses or applications are linked to this company. Unlink them first.";
        }

        return RedirectToAction(nameof(Index));
    }

    private static bool HasAnyValue(CompanyContactFormViewModel c) =>
        !string.IsNullOrWhiteSpace(c.PersonName) || !string.IsNullOrWhiteSpace(c.Title) ||
        !string.IsNullOrWhiteSpace(c.Phone) || !string.IsNullOrWhiteSpace(c.Email);

    private async Task PopulateDropdowns()
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        ViewBag.IsAdmin = isAdmin;

        var countriesQuery = isAdmin
            ? _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
            : _db.Countries.Where(c => c.Id == scopedCountryId);
        var countries = await countriesQuery.Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();

        ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");

        var originCountries = await _db.OriginCountries.Where(o => o.IsActive).OrderBy(o => o.Name).ToListAsync();
        ViewBag.CountryOfOriginOptions = new SelectList(originCountries, "Id", "Name");
    }
}
