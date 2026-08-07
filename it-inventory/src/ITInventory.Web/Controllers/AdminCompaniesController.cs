using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminCompaniesController : Controller
{
    private readonly ITInventoryDbContext _db;

    public AdminCompaniesController(ITInventoryDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var companies = await _db.Companies
            .Include(c => c.Contacts)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return View(companies);
    }

    public IActionResult Create()
    {
        var vm = new CompanyFormViewModel();
        vm.Contacts.Add(new CompanyContactFormViewModel());
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CompanyFormViewModel vm)
    {
        if (await _db.Companies.AnyAsync(c => c.Name == vm.Name))
            ModelState.AddModelError(nameof(vm.Name), "A company with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        var entity = new Company
        {
            Name = vm.Name,
            CountryOfOrigin = vm.CountryOfOrigin,
            IsActive = vm.IsActive,
            CreatedAt = DateTime.UtcNow
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
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var company = await _db.Companies.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id);
        if (company is null) return NotFound();

        var vm = new CompanyFormViewModel
        {
            Id = company.Id,
            Name = company.Name,
            CountryOfOrigin = company.CountryOfOrigin,
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

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CompanyFormViewModel vm)
    {
        if (id != vm.Id) return BadRequest();

        if (await _db.Companies.AnyAsync(c => c.Name == vm.Name && c.Id != id))
            ModelState.AddModelError(nameof(vm.Name), "A company with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        var entity = await _db.Companies.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id);
        if (entity is null) return NotFound();

        entity.Name = vm.Name;
        entity.CountryOfOrigin = vm.CountryOfOrigin;
        entity.IsActive = vm.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

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
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var company = await _db.Companies.FindAsync(id);
        if (company is null) return NotFound();

        _db.Companies.Remove(company);

        try
        {
            await _db.SaveChangesAsync();
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
}
