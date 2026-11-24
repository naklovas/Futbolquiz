using System.DirectoryServices.Protocols;
using System.Net;
using ITInventory.Web.Configuration;
using Microsoft.Extensions.Options;

namespace ITInventory.Web.Services;

public class LdapAuthenticationService : ILdapAuthenticationService
{
    private readonly LdapSettings _settings;
    private readonly ILogger<LdapAuthenticationService> _logger;

    public LdapAuthenticationService(IOptions<LdapSettings> settings, ILogger<LdapAuthenticationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public bool ValidateCredentials(string username, string password,
        out string? errorMessage, out string? eslesenDomain)
    {
        errorMessage = null;
        eslesenDomain = null;

        var domainler = _settings.EtkinDomainler;
        if (domainler.Count == 0 || domainler.All(d => string.IsNullOrWhiteSpace(d.Name)))
        {
            // Ldap bolumu hic yoksa boyle oluyordu: bos sunucu adina bind
            // denenip "parola yanlis" deniyordu. Sebebi artik aciktan yaziyoruz.
            _logger.LogError("Ldap yapilandirmasi eksik: tanimli domain yok.");
            errorMessage = "Authentication is not configured. Please contact your system administrator.";
            return false;
        }

        // Kullanici adresini tam yazdiysa domaini o belirler; yazmadiysa
        // hepsini sirayla deniyoruz.
        var denenecekler = domainler;
        if (username.Contains('@'))
        {
            var sonek = username[(username.IndexOf('@') + 1)..];
            var secilen = domainler.FirstOrDefault(d =>
                string.Equals(d.Name, sonek, StringComparison.OrdinalIgnoreCase));

            if (secilen is null)
            {
                _logger.LogWarning("Taninmayan domain sonegi ile giris denemesi: {Username}", username);
                errorMessage = "Invalid username or password.";
                return false;
            }

            denenecekler = new List<LdapDomainSettings> { secilen };
        }

        var ulasilamayanlar = new List<string>();

        foreach (var domain in denenecekler)
        {
            if (string.IsNullOrWhiteSpace(domain.Name)) continue;

            var kullaniciAdresi = username.Contains('@') ? username : $"{username}@{domain.Name}";

            switch (Dene(domain, kullaniciAdresi, password))
            {
                case Sonuc.Kabul:
                    eslesenDomain = domain.Name;
                    return true;

                case Sonuc.Ulasilamadi:
                    // Sunucuya ulasilamadiysa bunu "parola yanlis" diye
                    // gostermiyoruz; digerlerini deneyip sonunda soyluyoruz.
                    ulasilamayanlar.Add(domain.Name);
                    break;

                case Sonuc.Red:
                    break;
            }
        }

        // Hangi domaine ulasilamadigini YAZIYORUZ. Bunu yazmadan mesaj
        // "bir yere ulasilamadi" demekten ibaretti ve iki domainli bir
        // kurulumda hangisinin dustugunu kimse anlayamiyordu.
        errorMessage = ulasilamayanlar.Count > 0
            ? $"Unable to reach the authentication server for {string.Join(", ", ulasilamayanlar)}. Please try again later."
            : "Invalid username or password.";

        if (ulasilamayanlar.Count > 0)
            _logger.LogError("Ulasilamayan domain controller(lar): {Domainler}",
                string.Join(", ", ulasilamayanlar));

        return false;
    }

    private enum Sonuc { Kabul, Red, Ulasilamadi }

    /// <summary>
    /// Yapilandirmadaki metni AuthType'a cevirir. Taninmayan deger
    /// Negotiate'e duser ve loglanir: yazim hatasi yuzunden sessizce
    /// baska bir kimlik dogrulama turune gecmek, sonradan bulunmasi zor
    /// bir davranis farki yaratirdi.
    /// </summary>
    private AuthType TuruCozumle(string? ayar)
    {
        if (string.IsNullOrWhiteSpace(ayar)) return System.DirectoryServices.Protocols.AuthType.Negotiate;

        if (Enum.TryParse<AuthType>(ayar.Trim(), ignoreCase: true, out var tur))
            return tur;

        _logger.LogWarning("Taninmayan Ldap AuthType degeri: {Deger}. Negotiate kullaniliyor.", ayar);
        return System.DirectoryServices.Protocols.AuthType.Negotiate;
    }

    private Sonuc Dene(LdapDomainSettings domain, string kullaniciAdresi, string password)
    {
        var sunucu = domain.SunucuAdresi;
        var identifier = new LdapDirectoryIdentifier(sunucu, domain.Port);
        var credential = new NetworkCredential(kullaniciAdresi, password);

        using var connection = new LdapConnection(identifier, credential)
        {
            AuthType = TuruCozumle(domain.AuthType),
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (domain.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
            connection.SessionOptions.ProtocolVersion = 3;
        }

        try
        {
            connection.Bind();
            return Sonuc.Kabul;
        }
        catch (LdapException ex)
        {
            // Parola yanlis OLABILIR, ama sunucu ayakta degilse de buraya
            // dusuyoruz. Ayirt etmek icin hata koduna bakiyoruz: 81
            // (ServerDown) ve 85 (TimeLimitExceeded) ulasilamama demek.
            var ulasilamadi = ex.ErrorCode is 81 or 85;

            _logger.LogWarning(
                "LDAP bind basarisiz. Domain: {Domain}, Kullanici: {Kullanici}, Kod: {Kod}, Hata: {Mesaj}",
                domain.Name, kullaniciAdresi, ex.ErrorCode, ex.Message);

            return ulasilamadi ? Sonuc.Ulasilamadi : Sonuc.Red;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP sunucusuna baglanilamadi ({Sunucu}).", sunucu);
            return Sonuc.Ulasilamadi;
        }
    }
}
