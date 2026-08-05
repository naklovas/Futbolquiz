using System.ComponentModel.DataAnnotations;
using ITInventory.Data.Common;

namespace ITInventory.Web.Models.Servers;

public class ServerFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Ülke zorunludur.")]
    [Display(Name = "Ülke")]
    public int CountryId { get; set; }

    [Display(Name = "Cihaz Profili (havuzdan)")]
    public int? DeviceProfileId { get; set; }

    public int? SourceZiraatYdId { get; set; }

    [Required(ErrorMessage = "Host adı zorunludur.")]
    [StringLength(255)]
    [Display(Name = "Host Adı")]
    public string HostName { get; set; } = string.Empty;

    [Display(Name = "Fiziksel/Sanal")]
    public ApplianceType ApplianceType { get; set; }

    [StringLength(50)]
    [Display(Name = "IP Adresi")]
    public string? IpAddress { get; set; }

    [StringLength(255)]
    [Display(Name = "İşletim Sistemi")]
    public string? OperatingSystem { get; set; }

    [StringLength(100)]
    [Display(Name = "Marka")]
    public string? Brand { get; set; }

    [StringLength(150)]
    [Display(Name = "Model")]
    public string? Model { get; set; }

    [StringLength(150)]
    [Display(Name = "Seri No")]
    public string? SerialNo { get; set; }

    [StringLength(150)]
    [Display(Name = "Üretici/Yüklenici")]
    public string? VendorSupplier { get; set; }

    [Range(1, 65535)]
    [Display(Name = "Port")]
    public int? Port { get; set; }

    [StringLength(150)]
    [Display(Name = "Şube")]
    public string? Branch { get; set; }

    [Required(ErrorMessage = "Lokasyon zorunludur.")]
    [StringLength(255)]
    [Display(Name = "Lokasyon")]
    public string Location { get; set; } = string.Empty;

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
