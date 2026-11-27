namespace ITInventory.Web.Configuration;

/// <summary>
/// GELISTIRME ANAHTARI: acikken AD/LDAP dogrulamasi ATLANIR.
///
/// Neden parola yok:
/// Eski "test kullanicisi" ozelligi ikinci bir kimlik deposu ve PBKDF2
/// dogrulayici tasiyordu; salt kayitli metinden okundugu icin Fortify
/// "Weak Cryptographic Hash" raporladi ve ozellik tamamen silindi.
/// Burada SAKLANAN BIR SIR YOK -- dolayisiyla o bulgu geri gelmiyor.
/// Acikken, dbo.YDUsers icinde VAR OLAN bir kullanici adi yeterlidir;
/// parola alani hic kontrol edilmez.
///
/// NE ISE YARAR:
/// Ulke kapsamini gercek bir AD hesabi olmadan denemek. algeria_test,
/// germany_test gibi satirlar fintek.local'de bulunmadigi icin normalde
/// giris yapamiyor.
///
/// GUVENLIK:
/// Bu bayrak kimlik dogrulamasini TAMAMEN devre disi birakir. Uretimde
/// acik kalirsa kullanici adini bilen herkes o kullanici olarak girer.
/// Bu yuzden Program.cs, ortam Development DEGILKEN bayrak aciksa
/// uygulamayi BASLATMAZ: "proda giderken kapatiriz" bir hatirlama isi
/// olmaktan cikip zorunluluk haline geliyor.
///
/// Ayar yalnizca appsettings.Development.json icinde bulunmali;
/// appsettings.json'a yazilirsa yayin ciktisina da gider.
/// </summary>
public class DevLoginSettings
{
    public const string SectionName = "DevLogin";

    /// <summary>true ise LDAP atlanir. Varsayilan kapali.</summary>
    public bool Enabled { get; set; }
}
