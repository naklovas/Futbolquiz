using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Circuits;

public class CircuitFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Ülke zorunludur.")]
    [Display(Name = "Ülke")]
    public int CountryId { get; set; }

    [Required(ErrorMessage = "Hat tipi zorunludur.")]
    [StringLength(100)]
    [Display(Name = "Hat Tipi")]
    public string CircuitType { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "Kapasite")]
    public string? CircuitCapacity { get; set; }

    [StringLength(150)]
    [Display(Name = "Sağlayıcı")]
    public string? Provider { get; set; }

    [StringLength(150)]
    [Display(Name = "Şube")]
    public string? Branch { get; set; }

    [Required(ErrorMessage = "Lokasyon zorunludur.")]
    [StringLength(255)]
    [Display(Name = "Lokasyon")]
    public string Location { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Başlangıç Tarihi")]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Bitiş Tarihi")]
    public DateTime? EndDate { get; set; }

    [Display(Name = "Notlar")]
    public string? Notes { get; set; }
}
