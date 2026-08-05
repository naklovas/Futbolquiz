using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Licenses;

public class LicenseFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int CountryId { get; set; }

    [Required(ErrorMessage = "License name is required.")]
    [StringLength(255)]
    [Display(Name = "License Name")]
    public string LicenseName { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Vendor/Supplier")]
    public string? VendorSupplier { get; set; }

    [StringLength(150)]
    [Display(Name = "Branch")]
    public string? Branch { get; set; }

    [Required(ErrorMessage = "Location is required.")]
    [StringLength(255)]
    [Display(Name = "Location")]
    public string Location { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Support Start Date")]
    public DateTime? SupportStartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Support End Date")]
    public DateTime? SupportEndDate { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
