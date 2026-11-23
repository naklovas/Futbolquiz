namespace ITInventory.Web.Configuration;

/// <summary>
/// vROps / Aria Operations baglanti ayarlari.
///
/// PAROLA BURAYA YAZILMAZ. appsettings.json'a yazilan her sir kod taramasinda
/// "Password Management: Hardcoded Password" olarak raporlanir ve hakli olarak:
/// dosya kaynak kontrolune, yedeklere ve publish ciktisina gider.
///
/// Kullanici adi ve parola ortam degiskeninden okunur; .NET'in yapilandirma
/// saglayicisi bunlari kendiliginden bu sinifa baglar:
///
///     Vrops__Username = servis_hesabi
///     Vrops__Password = ...
///
/// IIS'te makine seviyesi ortam degiskeni guvenilmez (servis yoneticisi ortami
/// acilista onbellege alir). Application Pool ortam degiskeni kullanin --
/// baglanti dizesi icin de ayni yolu kullaniyoruz.
/// </summary>
public class VropsSettings
{
    public const string SectionName = "Vrops";

    /// <summary>Sunucu adi veya IP. https:// ve sondaki / olmadan.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Kimlik kaynagi: yerel hesap icin "Local", AD icin domain adi.</summary>
    public string AuthSource { get; set; } = "Local";

    /// <summary>Ortam degiskeninden gelir (Vrops__Username).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Ortam degiskeninden gelir (Vrops__Password).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Sunucu sertifikasini dogrulamayi atla.
    ///
    /// VARSAYILAN false, ve oyle kalmali. true yapmak, araya giren birinin
    /// kendi sertifikasiyla bu baglantiyi dinleyebilecegi anlamina gelir.
    /// vROps self-signed sertifika kullaniyorsa DOGRU cozum, o sertifikayi
    /// uygulama sunucusunun guvenilen koklerine eklemektir. Bu bayrak sadece
    /// bunun mumkun olmadigi durumlar icin var.
    /// </summary>
    public bool SkipCertificateValidation { get; set; } = false;

    /// <summary>Liste ekraninin vROps'u kac dakika onbellekten okuyacagi.</summary>
    public int CacheMinutes { get; set; } = 5;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);
}
