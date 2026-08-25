using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Admin;

public class LocationFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int? CountryId { get; set; }

    [Required(ErrorMessage = "Branch is required.")]
    [StringLength(200)]
    [Display(Name = "Branch")]
    public string Branch { get; set; } = string.Empty;

    [Required(ErrorMessage = "Class is required.")]
    [StringLength(100)]
    [Display(Name = "Class")]
    public string Class { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
