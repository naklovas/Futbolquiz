using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.PhysicalDevices;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class PhysicalDevicesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public PhysicalDevicesController(ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(int? countryId, int? categoryId)
    {
        var query = _db.PhysicalDevices
            .Include(d => d.Country)
            .Include(d => d.Category)
            .AsQueryable();

        if (!_currentUser.IsAdmin)
            query = query.Where(d => d.CountryId == _currentUser.CountryId);
        else if (countryId.HasValue)
            query = query.Where(d => d.CountryId == countryId.Value);

        if (categoryId.HasValue)
            query = query.Where(d => d.CategoryId == categoryId.Value);

        var items = await query
            .OrderBy(d => d.Country!.Name).ThenBy(d => d.DeviceName)
            .ToListAsync();

        ViewBag.Countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Categories = await _db.DeviceCategories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCountryId = countryId;
        ViewBag.SelectedCategoryId = categoryId;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        return View(items);
    }

    public async Task<IActionResult> Create(bool fromPool = false, int? sourceId = null, int? countryId = null, int? categoryId = null)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var vm = new PhysicalDeviceFormViewModel
        {
            CountryId = _currentUser.IsAdmin ? countryId ?? 0 : _currentUser.CountryId ?? 0,
            CategoryId = categoryId ?? 0
        };

        if (fromPool && sourceId.HasValue)
        {
            var source = await _db.ZiraatYds.FindAsync(sourceId.Value);
            if (source is not null)
            {
                vm.SourceZiraatYdId = source.Id;
                vm.DeviceName = source.DnsName ?? source.NetbiosName ?? source.IpAddress;
                vm.IpAddress = source.IpAddress;
                vm.SoftwareVersion = source.OperatingSystem;

                var profile = await _db.DeviceProfileCatalogs
                    .FirstOrDefaultAsync(p => p.ProfileName == source.DeviceProfile);
                if (profile is not null)
                {
                    vm.DeviceProfileId = profile.Id;
                    if (vm.CategoryId == 0 && profile.CategoryId.HasValue)
                        vm.CategoryId = profile.CategoryId.Value;
                }
            }
        }

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PhysicalDeviceFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new PhysicalDevice
        {
            CountryId = vm.CountryId,
            CategoryId = vm.CategoryId,
            DeviceProfileId = vm.DeviceProfileId,
            SourceZiraatYdId = vm.SourceZiraatYdId,
            DeviceName = vm.DeviceName,
            Brand = vm.Brand,
            Model = vm.Model,
            ApplianceType = vm.ApplianceType,
            SoftwareVersion = vm.SoftwareVersion,
            SerialNo = vm.SerialNo,
            IpAddress = vm.IpAddress,
            MgmtIp = vm.MgmtIp,
            Branch = vm.Branch,
            Location = vm.Location,
            VendorSupplier = vm.VendorSupplier,
            LicenceInfo = vm.LicenceInfo,
            StartOfSupportDate = vm.StartOfSupportDate,
            EndOfSupportDate = vm.EndOfSupportDate,
            EndOfLifeDate = vm.EndOfLifeDate,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.PhysicalDevices.Add(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.PhysicalDevices.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        var vm = new PhysicalDeviceFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            CategoryId = entity.CategoryId,
            DeviceProfileId = entity.DeviceProfileId,
            SourceZiraatYdId = entity.SourceZiraatYdId,
            DeviceName = entity.DeviceName,
            Brand = entity.Brand,
            Model = entity.Model,
            ApplianceType = entity.ApplianceType,
            SoftwareVersion = entity.SoftwareVersion,
            SerialNo = entity.SerialNo,
            IpAddress = entity.IpAddress,
            MgmtIp = entity.MgmtIp,
            Branch = entity.Branch,
            Location = entity.Location,
            VendorSupplier = entity.VendorSupplier,
            LicenceInfo = entity.LicenceInfo,
            StartOfSupportDate = entity.StartOfSupportDate,
            EndOfSupportDate = entity.EndOfSupportDate,
            EndOfLifeDate = entity.EndOfLifeDate,
            Notes = entity.Notes
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PhysicalDeviceFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.PhysicalDevices.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        entity.CountryId = vm.CountryId;
        entity.CategoryId = vm.CategoryId;
        entity.DeviceProfileId = vm.DeviceProfileId;
        entity.DeviceName = vm.DeviceName;
        entity.Brand = vm.Brand;
        entity.Model = vm.Model;
        entity.ApplianceType = vm.ApplianceType;
        entity.SoftwareVersion = vm.SoftwareVersion;
        entity.SerialNo = vm.SerialNo;
        entity.IpAddress = vm.IpAddress;
        entity.MgmtIp = vm.MgmtIp;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.LicenceInfo = vm.LicenceInfo;
        entity.StartOfSupportDate = vm.StartOfSupportDate;
        entity.EndOfSupportDate = vm.EndOfSupportDate;
        entity.EndOfLifeDate = vm.EndOfLifeDate;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.PhysicalDevices.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        _db.PhysicalDevices.Remove(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        var countries = _currentUser.IsAdmin
            ? await _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync()
            : await _db.Countries.Where(c => c.Id == _currentUser.CountryId).ToListAsync();

        ViewBag.CountryOptions = new SelectList(countries, "Id", "Name");
        ViewBag.CategoryOptions = new SelectList(await _db.DeviceCategories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
    }
}
