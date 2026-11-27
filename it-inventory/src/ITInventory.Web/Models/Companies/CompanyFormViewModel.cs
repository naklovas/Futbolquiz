using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;

namespace ITInventory.Web.Models.Companies;

public class CompanyFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int? CountryId { get; set; }

    [Required(ErrorMessage = "Company name is required.")]
    [StringLength(200)]
    [Display(Name = "Company Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Isaretlenen roller. Formda onay kutusu listesi; ayni firma hem
    /// tedarikci hem destek hem danismanlik olabiliyor.
    /// </summary>
    [Display(Name = "Roles")]
    public List<CompanyType> Roles { get; set; } = new();

    [StringLength(200)]
    [Display(Name = "Other Role (please specify)")]
    public string? OtherTypeDescription { get; set; }

    [Required(ErrorMessage = "Country of origin is required.")]
    [Display(Name = "Country of Origin")]
    public int? OriginCountryId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public List<CompanyContactFormViewModel> Contacts { get; set; } = new();

    /// <summary>
    /// Rol kumesinin kurallari. Tek tek alanlara yazilamiyor cunku ikisi de
    /// alanlar ARASINDAKI iliskiye bakiyor: en az bir rol secilmis olmali ve
    /// Other secildiyse aciklamasi girilmis olmali.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var roller = Roles.Distinct().ToList();

        if (roller.Count == 0)
            yield return new ValidationResult("Select at least one role.", new[] { nameof(Roles) });

        if (roller.Contains(CompanyType.Other) && string.IsNullOrWhiteSpace(OtherTypeDescription))
            yield return new ValidationResult("Please specify the Other role.", new[] { nameof(OtherTypeDescription) });
    }
}

public class CompanyContactFormViewModel
{
    public int Id { get; set; }

    [StringLength(150)]
    [Display(Name = "Name")]
    public string? PersonName { get; set; }

    [StringLength(150)]
    [Display(Name = "Title")]
    public string? Title { get; set; }

    [StringLength(50)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(150)]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email")]
    public string? Email { get; set; }
}
