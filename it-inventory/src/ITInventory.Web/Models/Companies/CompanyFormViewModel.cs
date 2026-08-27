using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;

namespace ITInventory.Web.Models.Companies;

public class CompanyFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int? CountryId { get; set; }

    [Required(ErrorMessage = "Company name is required.")]
    [StringLength(200)]
    [Display(Name = "Company Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Type")]
    public CompanyType CompanyType { get; set; } = CompanyType.Vendor;

    [StringLength(200)]
    [Display(Name = "Other Type (please specify)")]
    public string? OtherTypeDescription { get; set; }

    [Required(ErrorMessage = "Country of origin is required.")]
    [Display(Name = "Country of Origin")]
    public int? OriginCountryId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public List<CompanyContactFormViewModel> Contacts { get; set; } = new();
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
