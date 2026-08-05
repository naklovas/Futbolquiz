using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Admin;

public class CountryFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Ülke adı zorunludur.")]
    [StringLength(150)]
    [Display(Name = "Ülke Adı")]
    public string Name { get; set; } = string.Empty;

    [StringLength(20)]
    [Display(Name = "Kod")]
    public string? Code { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;
}
