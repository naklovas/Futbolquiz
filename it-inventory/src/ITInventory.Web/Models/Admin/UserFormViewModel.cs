using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Admin;

public class UserFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [StringLength(100)]
    [Display(Name = "Kullanıcı Adı (AD sAMAccountName)")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ad Soyad zorunludur.")]
    [StringLength(100)]
    [Display(Name = "Ad Soyad")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(100)]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [Display(Name = "E-posta")]
    public string? Email { get; set; }

    [Display(Name = "Ülke/Şube")]
    public int? CountryId { get; set; }

    [Display(Name = "Aktif")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Roller")]
    public List<int> SelectedRoleIds { get; set; } = new();
}
