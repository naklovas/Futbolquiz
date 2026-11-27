using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Admin;

public class CountryFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country name is required.")]
    [StringLength(150)]
    [Display(Name = "Country Name (must match Nessus RepositoryName)")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Display Name (e.g. institution name)")]
    public string? DisplayName { get; set; }

    [StringLength(20)]
    [Display(Name = "Code")]
    public string? Code { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
