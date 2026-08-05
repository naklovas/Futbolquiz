using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;

namespace ITInventory.Web.Models.Servers;

public class ServerFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int CountryId { get; set; }

    [Display(Name = "Device Profile (from pool)")]
    public int? DeviceProfileId { get; set; }

    public int? SourceZiraatYdId { get; set; }

    [Required(ErrorMessage = "Host name is required.")]
    [StringLength(255)]
    [Display(Name = "Host Name")]
    public string HostName { get; set; } = string.Empty;

    [Display(Name = "Physical/Virtual")]
    public ApplianceType ApplianceType { get; set; }

    [StringLength(50)]
    [Display(Name = "IP Address")]
    public string? IpAddress { get; set; }

    [StringLength(255)]
    [Display(Name = "Operating System")]
    public string? OperatingSystem { get; set; }

    [StringLength(100)]
    [Display(Name = "Brand")]
    public string? Brand { get; set; }

    [StringLength(150)]
    [Display(Name = "Model")]
    public string? Model { get; set; }

    [StringLength(150)]
    [Display(Name = "Serial Number")]
    public string? SerialNo { get; set; }

    [StringLength(150)]
    [Display(Name = "Vendor/Supplier")]
    public string? VendorSupplier { get; set; }

    [Range(1, 65535)]
    [Display(Name = "Port")]
    public int? Port { get; set; }

    [StringLength(150)]
    [Display(Name = "Branch")]
    public string? Branch { get; set; }

    [Required(ErrorMessage = "Location is required.")]
    [StringLength(255)]
    [Display(Name = "Location")]
    public string Location { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Support Start Date")]
    public DateTime? StartOfSupportDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Support End Date")]
    public DateTime? EndOfSupportDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "End of Life Date")]
    public DateTime? EndOfLifeDate { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
