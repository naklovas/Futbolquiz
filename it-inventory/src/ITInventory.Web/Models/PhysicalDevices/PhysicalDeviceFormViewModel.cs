using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;
using ITInventory.Web.Common;

namespace ITInventory.Web.Models.PhysicalDevices;

public class PhysicalDeviceFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int CountryId { get; set; }

    [Required(ErrorMessage = "Category is required.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Display(Name = "Device Profile (from pool)")]
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
    public SiteRole? SiteRole { get; set; }

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

    [Required(ErrorMessage = "Branch is required.")]
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

    [Required(ErrorMessage = "Support start date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Support Start Date")]
    public DateTime? StartOfSupportDate { get; set; }

    [Required(ErrorMessage = "Support end date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Support End Date")]
    public DateTime? EndOfSupportDate { get; set; }

    [Required(ErrorMessage = "End of life date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "End of Life Date")]
    public DateTime? EndOfLifeDate { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
