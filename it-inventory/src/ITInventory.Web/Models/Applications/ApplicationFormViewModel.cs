using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;

namespace ITInventory.Web.Models.Applications;

public class ApplicationFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int? CountryId { get; set; }

    [Required(ErrorMessage = "Application name is required.")]
    [StringLength(255)]
    [Display(Name = "Application Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Company is required.")]
    [Display(Name = "Company")]
    public int? CompanyId { get; set; }

    [Required(ErrorMessage = "License is required.")]
    [Display(Name = "License")]
    public int? LicenseId { get; set; }

    [Required(ErrorMessage = "Application type is required.")]
    [Display(Name = "Application Type")]
    public ApplicationType? ApplicationType { get; set; }

    [Display(Name = "Externally Exposed?")]
    public bool IsExternallyExposed { get; set; }

    [StringLength(500)]
    [Url(ErrorMessage = "Enter a valid URL.")]
    [Display(Name = "URL")]
    public string? Url { get; set; }

    [Display(Name = "Cloud Application?")]
    public bool IsCloudApplication { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
