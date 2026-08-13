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

    /// <summary>
    /// Only shown/used on Create (a Server can end up with several ServerEndpoints over time,
    /// so there's no single "the" IP to edit once one exists -- see the Server Endpoints page
    /// for that). Pre-filled from the Device Pool row when creating "from pool"; saving with
    /// this set creates the server's first ServerEndpoint.
    /// </summary>
    [StringLength(50)]
    [Display(Name = "IP Address")]
    public string? IpAddress { get; set; }

    [Required(ErrorMessage = "Host name is required.")]
    [StringLength(255)]
    [Display(Name = "Host Name")]
    public string HostName { get; set; } = string.Empty;

    [Display(Name = "Physical/Virtual")]
    public ApplianceType ApplianceType { get; set; }

    [Display(Name = "Location Category")]
    public LocationCategory LocationCategory { get; set; }

    [Display(Name = "ESXi / Physical Server")]
    public int? HostPhysicalDeviceId { get; set; }

    [Required(ErrorMessage = "Operating system is required.")]
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

    [Required(ErrorMessage = "Branch is required.")]
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
