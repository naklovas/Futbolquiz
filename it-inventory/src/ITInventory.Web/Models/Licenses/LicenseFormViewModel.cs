using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Licenses;

public class LicenseFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int? CountryId { get; set; }

    [Required(ErrorMessage = "License name is required.")]
    [StringLength(255)]
    [Display(Name = "License Name")]
    public string LicenseName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vendor/Supplier is required.")]
    [StringLength(150)]
    [Display(Name = "Vendor/Supplier")]
    public string? VendorSupplier { get; set; }

    [Required(ErrorMessage = "Company is required.")]
    [Display(Name = "Company")]
    public int? CompanyId { get; set; }

    [Required(ErrorMessage = "Branch is required.")]
    [StringLength(150)]
    [Display(Name = "Branch")]
    public string? Branch { get; set; }

    [StringLength(255)]
    [Display(Name = "Location")]
    public string? Location { get; set; }

    [Required(ErrorMessage = "Support start date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Support Start Date")]
    public DateTime? SupportStartDate { get; set; }

    [Required(ErrorMessage = "Support end date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Support End Date")]
    public DateTime? SupportEndDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "License Expiration Date")]
    public DateTime? ExpirationDate { get; set; }

    [Display(Name = "Perpetual licence (no expiry date)")]
    public bool IsPerpetual { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Bitis tarihi KOSULLU zorunlu: suresiz isaretlenmediyse gerekli.
    /// Alani tamamen opsiyonel yapmak, "henuz girilmedi" ile "hic bitmiyor"
    /// arasini silerdi.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (IsPerpetual && ExpirationDate.HasValue)
            yield return new ValidationResult(
                "A perpetual licence cannot have an expiry date. Clear the date or untick Perpetual.",
                new[] { nameof(ExpirationDate) });

        if (!IsPerpetual && !ExpirationDate.HasValue)
            yield return new ValidationResult(
                "Enter the expiry date, or tick Perpetual if the licence does not expire.",
                new[] { nameof(ExpirationDate) });
    }
}
