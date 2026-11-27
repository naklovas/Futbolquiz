using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;
using ITInventory.Web.Common;

namespace ITInventory.Web.Models.PhysicalDevices;

public class PhysicalDeviceFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int? CountryId { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    [Display(Name = "Category")]
    public int? CategoryId { get; set; }

    // Eskiden yalnizca Cihaz Havuzu'ndan aktarimda doluyordu ve formda gizliydi;
    // artik kategoriye gore suzulmus bir liste olarak secilebiliyor.
    [Display(Name = "Device Profile")]
    public int? DeviceProfileId { get; set; }

    public int? SourceZiraatYdId { get; set; }

    [Required(ErrorMessage = "Device name is required.")]
    [StringLength(255)]
    [Display(Name = "Device Name")]
    public string DeviceName { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Brand")]
    public string? Brand { get; set; }

    [Required(ErrorMessage = "Model is required.")]
    [StringLength(150)]
    [Display(Name = "Model")]
    public string? Model { get; set; }

    [Display(Name = "Physical/Virtual")]
    public ApplianceType ApplianceType { get; set; }

    [Required(ErrorMessage = "Location category is required.")]
    [Display(Name = "Location Category")]
    public LocationCategory? LocationCategory { get; set; }

    [Required(ErrorMessage = "Site role is required.")]
    [Display(Name = "Site Role")]
    public SiteRole? SiteClassification { get; set; }

    [Required(ErrorMessage = "Software version is required.")]
    [StringLength(150)]
    [Display(Name = "Software Version")]
    public string? SoftwareVersion { get; set; }

    [Required(ErrorMessage = "Serial number is required.")]
    [StringLength(150)]
    [Display(Name = "Serial Number")]
    public string? SerialNo { get; set; }

    [Required(ErrorMessage = "IP address is required.")]
    [StringLength(50)]
    [IpAddress]
    [Display(Name = "IP Address")]
    public string? IpAddress { get; set; }

    [Required(ErrorMessage = "Management IP is required.")]
    [StringLength(50)]
    [IpAddress]
    [Display(Name = "Management IP")]
    public string? MgmtIp { get; set; }

    // [Required] degil: Location Category = Cloud secildiginde ekran Branch
    // alanini bosaltip kilitliyor (site.js initLocationCategoryToggle), cunku
    // bulutta sube yok. Zorunluluk sabit kalinca o kayit "Branch is required."
    // ile hic kaydedilemiyordu. Kural artik Validate() icinde, kategoriye bagli.
    [StringLength(150)]
    [Display(Name = "Branch")]
    public string? Branch { get; set; }

    [StringLength(255)]
    [Display(Name = "Location")]
    public string? Location { get; set; }

    [Required(ErrorMessage = "Vendor/Supplier is required.")]
    [StringLength(150)]
    [Display(Name = "Vendor/Supplier")]
    public string? VendorSupplier { get; set; }

    [Required(ErrorMessage = "License info is required.")]
    [StringLength(150)]
    [Display(Name = "License Info")]
    public string? LicenceInfo { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Support Start Date")]
    public DateTime? StartOfSupportDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Support End Date")]
    public DateTime? EndOfSupportDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "End of Life Date")]
    public DateTime? EndOfLifeDate { get; set; }

    [Display(Name = "End of life not announced yet")]
    public bool EndOfLifeNotAnnounced { get; set; }

    [Display(Name = "No separate hardware support contract")]
    public bool NoSupportContract { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Tarihler KOSULLU zorunlu. Donanimin ayri bakim anlasmasi yoksa ya da
    /// uretici omur sonu tarihini henuz aciklamadiysa bos kalabilirler --
    /// ama bu bilerek yapilmis bir secim olmali, unutulmus bir alan degil.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // Bulut disindaki her cihaz bir subede duruyor; bulutta sube yok ve
        // ekran alani zaten bosaltip kilitliyor.
        if (LocationCategory != Data.Common.LocationCategory.Cloud && string.IsNullOrWhiteSpace(Branch))
            yield return new ValidationResult(
                "Branch is required.",
                new[] { nameof(Branch) });

        if (!NoSupportContract && !StartOfSupportDate.HasValue)
            yield return new ValidationResult(
                "Enter the support start date, or tick the no-contract option.",
                new[] { nameof(StartOfSupportDate) });

        if (!NoSupportContract && !EndOfSupportDate.HasValue)
            yield return new ValidationResult(
                "Enter the support end date, or tick the no-contract option.",
                new[] { nameof(EndOfSupportDate) });

        if (NoSupportContract && (StartOfSupportDate.HasValue || EndOfSupportDate.HasValue))
            yield return new ValidationResult(
                "Clear the support dates, or untick the no-contract option.",
                new[] { nameof(StartOfSupportDate) });

        if (!EndOfLifeNotAnnounced && !EndOfLifeDate.HasValue)
            yield return new ValidationResult(
                "Enter the end of life date, or tick that it has not been announced.",
                new[] { nameof(EndOfLifeDate) });

        if (EndOfLifeNotAnnounced && EndOfLifeDate.HasValue)
            yield return new ValidationResult(
                "Clear the end of life date, or untick the not-announced option.",
                new[] { nameof(EndOfLifeDate) });
    }
}
