using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Admin;

public class OriginCountryFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country name is required.")]
    [StringLength(150)]
    [Display(Name = "Country Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
