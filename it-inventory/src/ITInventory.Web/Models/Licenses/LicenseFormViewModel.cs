using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Licenses;

public class LicenseFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Ülke zorunludur.")]
    [Display(Name = "Ülke")]
    public int CountryId { get; set; }

    [Required(ErrorMessage = "Lisans adı zorunludur.")]
    [StringLength(255)]
    [Display(Name = "Lisans Adı")]
    public string LicenseName { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Üretici/Yüklenici")]
    public string? VendorSupplier { get; set; }

    [StringLength(150)]
    [Display(Name = "Şube")]
    public string? Branch { get; set; }

    [Required(ErrorMessage = "Lokasyon zorunludur.")]
    [StringLength(255)]
    [Display(Name = "Lokasyon")]
    public string Location { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Destek Başlangıç Tarihi")]
    public DateTime? SupportStartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Destek Bitiş Tarihi")]
    public DateTime? SupportEndDate { get; set; }

    [Display(Name = "Notlar")]
    public string? Notes { get; set; }
}
