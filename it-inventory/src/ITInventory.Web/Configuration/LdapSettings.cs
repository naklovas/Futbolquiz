namespace ITInventory.Web.Configuration;

/// <summary>
/// Tek bir AD domaininin baglanti bilgisi.
/// </summary>
public class LdapDomainSettings
{
    /// <summary>Domain adi. Ornek: fintek.local</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Domain controller adlari/IP'leri. Bossa Name DNS uzerinden cozulur.</summary>
    public string[] Servers { get; set; } = Array.Empty<string>();

    public int Port { get; set; } = 636;
    public bool UseSsl { get; set; } = true;

    public string SunucuAdresi => Servers.Length > 0 ? Servers[0] : Name;
}

/// <summary>
/// LDAP ayarlari.
///
/// BIRDEN FAZLA DOMAIN:
/// Kullanicilar artik tek bir domainden gelmiyor (fintek.local ve
/// ZFGISTIRAK birlikte). "Domains" listesi doldurulursa o kullanilir;
/// bos birakilirsa asagidaki tekil alanlar tek domainlik bir liste gibi
/// davranir, boylece eski appsettings.json dosyalari calismaya devam eder.
///
/// SIRA ONEMLI: kullanici adini domainsiz yazdiginda domainler bu sirayla
/// denenir ve ilk kabul eden kazanir. Ana domaini basa koyun.
/// </summary>
public class LdapSettings
{
    public const string SectionName = "Ldap";

    // --- eski tek-domain bicimi (geriye donuk uyumluluk) ---

    /// <summary>Örn: fintek.local</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Domain controller adları/IP'leri. Boşsa Domain adı DNS üzerinden çözülür.</summary>
    public string[] Servers { get; set; } = Array.Empty<string>();

    public int Port { get; set; } = 636;
    public bool UseSsl { get; set; } = true;

    // --- yeni cok-domain bicimi ---

    public List<LdapDomainSettings> Domains { get; set; } = new();

    /// <summary>
    /// Gercekte kullanilacak domain listesi. "Domains" doluysa o, degilse
    /// tekil alanlardan uretilmis tek elemanli liste.
    /// </summary>
    public IReadOnlyList<LdapDomainSettings> EtkinDomainler =>
        Domains.Count > 0
            ? Domains
            : new List<LdapDomainSettings>
            {
                new()
                {
                    Name = Domain,
                    Servers = Servers,
                    Port = Port,
                    UseSsl = UseSsl
                }
            };

    /// <summary>
    /// Varsayilan domain -- listenin ilki. dbo.YDUsers icindeki Domain
    /// kolonu bos olan ESKI kayitlar bu domaine ait sayilir.
    /// </summary>
    public string VarsayilanDomain => EtkinDomainler.FirstOrDefault()?.Name ?? string.Empty;
}
