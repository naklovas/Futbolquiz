using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;

namespace ITInventory.Web.Models.PhysicalDevices;

public class PhysicalDeviceFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Ülke zorunludur.")]
    [Display(Name = "Ülke")]
    public int CountryId { get; set; }

    [Required(ErrorMessage = "Kategori zorunludur.")]
    [Display(Name = "Kategori")]
    public int CategoryId { get; set; }

    [Display(Name = "Cihaz Profili (havuzdan)")]
    public int? DeviceProfileId { get; set; }

    public int? SourceZiraatYdId { get; set; }

    [Required(ErrorMessage = "Cihaz adı zorunludur.")]
    [StringLength(255)]
    [Display(Name = "Cihaz Adı")]
    public string DeviceName { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Marka")]
    public string? Brand { get; set; }

    [StringLength(150)]
    [Display(Name = "Model")]
    public string? Model { get; set; }

    [Display(Name = "Fiziksel/Sanal")]
    public ApplianceType ApplianceType { get; set; }

    [StringLength(150)]
    [Display(Name = "Yazılım Versiyonu")]
    public string? SoftwareVersion { get; set; }

    [StringLength(150)]
    [Display(Name = "Seri No")]
    public string? SerialNo { get; set; }

    [StringLength(50)]
    [Display(Name = "IP Adresi")]
    public string? IpAddress { get; set; }

    [StringLength(50)]
    [Display(Name = "Yönetim IP")]
    public string? MgmtIp { get; set; }

    [StringLength(150)]
    [Display(Name = "Şube")]
    public string? Branch { get; set; }

    [Required(ErrorMessage = "Lokasyon zorunludur.")]
    [StringLength(255)]
    [Display(Name = "Lokasyon")]
    public string Location { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Üretici/Yüklenici")]
    public string? VendorSupplier { get; set; }

    [StringLength(150)]
    [Display(Name = "Lisans Bilgisi")]
    public string? LicenceInfo { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Destek Başlangıç Tarihi")]
    public DateTime? StartOfSupportDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Destek Bitiş Tarihi")]
    public DateTime? EndOfSupportDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "End of Life Tarihi")]
    public DateTime? EndOfLifeDate { get; set; }

    [Display(Name = "Notlar")]
    public string? Notes { get; set; }
}
