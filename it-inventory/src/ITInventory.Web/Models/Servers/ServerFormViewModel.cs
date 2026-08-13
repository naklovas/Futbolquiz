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

    /// <summary>IP address carried over from the selected Device Pool row when creating "from pool" -- used to pre-create a ServerEndpoint. Not shown/edited in the form.</summary>
    public string? PoolIpAddress { get; set; }

    [Required(ErrorMessage = "Host name is required.")]
    [StringLength(255)]
    [Display(Name = "Host Name")]
    public string HostName { get; set; } = string.Empty;

    [Display(Name = "Physical/Virtual")]
    public ApplianceType ApplianceType { get; set; }

    [Display(Name = "Location Category")]
    public LocationCategory LocationCategory { get; set; }

    [Display(Name = "Host (ESX/Physical Device)")]
    public int? HostPhysicalDeviceId { get; set; }

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

    [StringLength(150)]
    [Display(Name = "Branch")]
    public string? Branch { get; set; }

    [StringLength(255)]
    [Display(Name = "Location")]
    public string? Location { get; set; }

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
