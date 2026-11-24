using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "Username is required.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Girisin yapilacagi AD domaini. Birden fazla domain tanimliysa giris
    /// ekraninda secilir; tek domain varsa alan hic gosterilmez.
    ///
    /// Kullanici adini tam adres olarak yazmak (ali@zfgistirak.local) da
    /// calisiyor ve o yazim bu secimi EZER -- kisinin acikca yazdigi sey,
    /// listede kalmis bir secimden onceliklidir.
    /// </summary>
    [Display(Name = "Domain")]
    public string? Domain { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
