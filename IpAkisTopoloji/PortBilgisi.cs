// ---------------------------------------------------------------------------
// AI etki analizi için port bilgi tabanı: her portun ne işe yaradığı ve kesilirse hangi İŞİN durduğu.
// Küçük model port adından ("MS-RPC etkilenir") öteye gidemediği için etki cümlesi kodda hazırlanıp
// prompt'a konur; model bunu kaynak uygulama/IP'lerle birleştirerek yazar.
//   Gelen = sorgulanan sunucu bu portu SUNUYOR; sunucu kapanırsa kaynakların yapamayacağı iş.
//   Giden = sorgulanan sunucu bu porta BAĞLANIYOR; hedefe ulaşamazsa bu sunucunun yapamayacağı iş.
// appsettings "Ai:PortAciklamalari": { "8443": "Ödeme API'si" } ile kuruma özel portlar eklenebilir / değiştirilebilir.
// ---------------------------------------------------------------------------
record PortInfo(string Name, string Gelen, string Giden);

static class PortBilgisi
{
    static readonly Dictionary<string, PortInfo> Known = new()
    {
        ["20"] = new("FTP veri", "kaynaklar bu sunucuya FTP ile dosya gönderip alamaz (dosya aktarım işleri durur)", "bu sunucu hedefe FTP ile dosya gönderip alamaz"),
        ["21"] = new("FTP", "kaynaklar bu sunucuya FTP ile dosya gönderip alamaz; dosya aktarım/batch işleri hata verir", "bu sunucunun hedefe FTP ile yaptığı dosya aktarımları (batch, rapor, entegrasyon dosyaları) durur"),
        ["22"] = new("SSH/SFTP", "kaynaklar bu sunucuya SSH ile bağlanıp yönetemez, SFTP/SCP dosya transferleri ve SSH ile tetiklenen otomasyon işleri (script, deploy, yedek) çalışmaz", "bu sunucunun hedefe SSH/SFTP ile yaptığı dosya aktarımları, uzak komut ve otomasyon işleri durur"),
        ["23"] = new("Telnet", "kaynaklar bu sunucuya Telnet ile bağlanamaz", "bu sunucu hedefe Telnet ile bağlanamaz"),
        ["25"] = new("SMTP", "bu sunucu e-posta sunucusu/relay ise kaynakların gönderdiği e-postalar (bildirim, alarm, rapor e-postaları) iletilemez", "bu sunucunun gönderdiği e-postalar (bildirim, alarm, rapor) gitmez"),
        ["53"] = new("DNS", "bu sunucuyu DNS olarak kullanan kaynaklar isim çözümleyemez; isimle bağlandıkları TÜM uygulamalara erişimleri bozulur", "bu sunucu isim çözümleyemez; isimle bağlandığı tüm hedeflere (veritabanı, servis, AD) erişimi bozulur"),
        ["67"] = new("DHCP", "kaynaklar IP adresi alamaz / yenileyemez", "bu sunucu IP adresi alamaz / yenileyemez"),
        ["69"] = new("TFTP", "kaynaklar TFTP ile dosya/konfigürasyon (ağ cihazı, PXE boot) alamaz", "bu sunucu TFTP ile dosya alamaz"),
        ["80"] = new("HTTP", "kaynakların bu sunucudaki web uygulamasına / servis çağrılarına (API, web servis) erişimi kesilir", "bu sunucunun hedefe yaptığı web/API çağrıları başarısız olur; o servise bağlı iş akışları hata verir"),
        ["88"] = new("Kerberos", "bu sunucu domain controller ise kaynaklar Windows/AD kimlik doğrulaması yapamaz (oturum açma, servis hesabı ile erişim, SSO çalışmaz)", "bu sunucu Kerberos ile kimlik doğrulayamaz; Windows oturumu, servis hesaplarıyla erişim ve SSO bozulur"),
        ["111"] = new("RPCbind", "kaynaklar bu sunucudaki NFS/RPC servislerini bulamaz (NFS mount'ları bozulur)", "bu sunucu hedefteki NFS/RPC servislerine bağlanamaz"),
        ["123"] = new("NTP", "bu sunucudan saat alan kaynakların saati kayar; zamanla Kerberos oturum açma ve log/işlem zaman damgaları bozulur", "bu sunucunun saati senkronize olmaz; zamanla Kerberos oturum açma hataları ve yanlış zaman damgaları oluşur"),
        ["135"] = new("MS-RPC (Endpoint Mapper)", "kaynaklar bu sunucunun Windows RPC servislerine bağlanamaz: uzaktan yönetim (WMI, Hizmetler/Olay Görüntüleyici, Computer Management), DCOM uygulamaları, zamanlanmış görevlerin uzaktan yönetimi, bu sunucu DC ise AD replikasyonu ve domain işlemleri, izleme/yedekleme ajanlarının WMI sorguları çalışmaz", "bu sunucu hedefin RPC servislerini kullanamaz: hedef DC ise domain işlemleri/grup ilkesi, WMI ile yönetim-izleme, DCOM çağrıları başarısız olur"),
        ["137"] = new("NetBIOS isim", "kaynaklar bu sunucuyu NetBIOS adıyla bulamaz (eski Windows paylaşım/uygulama erişimleri)", "bu sunucu NetBIOS isim çözümleyemez"),
        ["138"] = new("NetBIOS datagram", "eski Windows ağ gezintisi/duyuruları çalışmaz", "eski Windows ağ gezintisi/duyuruları çalışmaz"),
        ["139"] = new("NetBIOS oturum / SMB", "kaynaklar bu sunucudaki paylaşımlı klasörlere (eski SMB) erişemez", "bu sunucu hedefteki paylaşımlara (eski SMB) erişemez"),
        ["161"] = new("SNMP", "izleme sistemleri bu sunucudan performans/durum bilgisi toplayamaz; alarmlar ve grafikler boş kalır", "bu sunucu hedef cihazları SNMP ile izleyemez"),
        ["162"] = new("SNMP Trap", "cihazların gönderdiği alarm (trap) mesajları bu sunucuya ulaşmaz; olaylar fark edilmez", "bu sunucunun gönderdiği alarm (trap) mesajları izleme sistemine ulaşmaz"),
        ["389"] = new("LDAP", "bu sunucu DC/dizin sunucusu ise kaynaklar kullanıcı/grup sorgulayamaz; LDAP ile oturum açan uygulamalarda kullanıcı girişi ve yetki kontrolleri çalışmaz", "bu sunucudaki uygulamalar LDAP ile kullanıcı doğrulayamaz / grup sorgulayamaz; kullanıcı girişi ve yetki kontrolleri bozulur"),
        ["443"] = new("HTTPS", "kaynakların bu sunucudaki web uygulamasına / servis çağrılarına (API, web servis) erişimi kesilir", "bu sunucunun hedefe yaptığı web/API çağrıları başarısız olur; o servise bağlı iş akışları hata verir"),
        ["445"] = new("SMB (dosya paylaşımı)", "kaynaklar bu sunucudaki paylaşımlı klasörlere erişemez: dosya okuma/yazma, dosya bırakan batch işleri, yedek kopyaları; sunucu DC ise grup ilkesi (SYSVOL) ve oturum açma betikleri çalışmaz", "bu sunucu hedefteki paylaşımlı klasörlere dosya yazıp okuyamaz (dosya aktarımı, yedek, rapor bırakma işleri durur); hedef DC ise grup ilkesi uygulanamaz"),
        ["464"] = new("Kerberos şifre değiştirme", "kaynaklar domain şifresi değiştiremez / sıfırlayamaz", "bu sunucu domain şifre değişikliği yapamaz"),
        ["465"] = new("SMTPS", "kaynakların şifreli e-posta gönderimi durur", "bu sunucunun şifreli e-posta gönderimi durur"),
        ["514"] = new("Syslog", "kaynakların log kayıtları bu sunucuya ulaşmaz; güvenlik/denetim logları kaybolur (SIEM'de boşluk oluşur)", "bu sunucunun logları merkezi log/SIEM sistemine gitmez; denetim izi kaybolur"),
        ["587"] = new("SMTP gönderim", "kaynakların e-posta gönderimi durur", "bu sunucunun e-posta gönderimi (bildirim, rapor) durur"),
        ["636"] = new("LDAPS", "kaynaklar şifreli LDAP ile kullanıcı doğrulayamaz; LDAPS kullanan uygulamalarda kullanıcı girişi çalışmaz", "bu sunucudaki uygulamalar şifreli LDAP ile kullanıcı doğrulayamaz; kullanıcı girişi bozulur"),
        ["873"] = new("rsync", "kaynakların bu sunucuyla dosya eşitlemesi/yedeği durur", "bu sunucunun hedefle dosya eşitlemesi/yedeği durur"),
        ["902"] = new("VMware yönetim", "vCenter bu ESXi sunucusunu yönetemez, sanal makine konsolları açılmaz", "bu sunucu VMware hostlarını yönetemez / konsol açamaz"),
        ["1433"] = new("MS SQL Server", "kaynak uygulamalar bu sunucudaki SQL Server veritabanına bağlanamaz; veritabanını kullanan TÜM ekranlar, servisler ve batch işleri hata verir", "bu sunucudaki uygulama SQL Server veritabanına bağlanamaz; veriye dayanan tüm işlemleri durur"),
        ["1434"] = new("SQL Server Browser", "kaynaklar isimli SQL instance'larının portunu bulamaz (instance adıyla bağlanan uygulamalar bağlanamaz)", "bu sunucu isimli SQL instance'larına instance adıyla bağlanamaz"),
        ["1521"] = new("Oracle", "kaynak uygulamalar bu sunucudaki Oracle veritabanına bağlanamaz; veritabanını kullanan TÜM işlemler hata verir", "bu sunucudaki uygulama Oracle veritabanına bağlanamaz; veriye dayanan tüm işlemleri durur"),
        ["2049"] = new("NFS", "bu sunucudan NFS disk bağlayan (mount) kaynaklarda paylaşılan dizinler erişilemez olur; bu dizinleri kullanan uygulamalar askıda kalabilir", "bu sunucunun hedeften bağladığı NFS dizinlerine erişim kesilir; o dizinleri kullanan uygulamalar askıda kalabilir"),
        ["3268"] = new("AD Global Catalog", "kaynaklar orman genelinde kullanıcı/grup arayamaz; çok domainli oturum açma ve Exchange/uygulama adres aramaları bozulur", "bu sunucu Global Catalog sorgulayamaz; çok domainli kullanıcı aramaları bozulur"),
        ["3269"] = new("AD Global Catalog (SSL)", "kaynaklar şifreli Global Catalog sorgusu yapamaz", "bu sunucu şifreli Global Catalog sorgusu yapamaz"),
        ["3306"] = new("MySQL", "kaynak uygulamalar bu sunucudaki MySQL veritabanına bağlanamaz; veriye dayanan tüm işlemleri durur", "bu sunucudaki uygulama MySQL veritabanına bağlanamaz"),
        ["3389"] = new("RDP (Uzak Masaüstü)", "bu kaynaklardan bu sunucuya yapılan uzak masaüstü (RDP) bağlantıları kurulamaz; sunucuya RDP ile bağlanıp yönetim/operasyon yapan kullanıcılar, jump/PAM sunucuları üzerinden gelen yöneticiler ve RDP ile çalışan uygulama kullanıcıları erişemez", "bu sunucudan hedefe uzak masaüstü (RDP) bağlantısı kurulamaz; bu sunucu jump/PAM sunucusuysa yöneticiler hedefe bu yoldan ulaşamaz"),
        ["5432"] = new("PostgreSQL", "kaynak uygulamalar bu sunucudaki PostgreSQL veritabanına bağlanamaz; veriye dayanan tüm işlemleri durur", "bu sunucudaki uygulama PostgreSQL veritabanına bağlanamaz"),
        ["5666"] = new("Nagios NRPE", "izleme sistemi bu sunucuyu kontrol edemez; alarmlar gelmez", "bu sunucu hedefleri Nagios ile kontrol edemez"),
        ["5985"] = new("WinRM (PowerShell uzak yönetim)", "kaynaklar bu sunucuyu PowerShell Remoting/WinRM ile yönetemez; otomasyon (Ansible, SCCM, script), deploy ve izleme ajanlarının uzak komutları çalışmaz", "bu sunucu hedefi PowerShell Remoting/WinRM ile yönetemez; otomasyon ve deploy işleri durur"),
        ["5986"] = new("WinRM HTTPS", "kaynaklar bu sunucuyu şifreli WinRM ile yönetemez; otomasyon/deploy işleri çalışmaz", "bu sunucu hedefi şifreli WinRM ile yönetemez"),
        ["6379"] = new("Redis", "kaynak uygulamalar önbellek/oturum verisine ulaşamaz; oturumlar düşebilir, uygulamalar yavaşlar veya hata verir", "bu sunucudaki uygulama Redis önbelleğine/oturum deposuna ulaşamaz; yavaşlama ve oturum kayıpları olur"),
        ["8080"] = new("HTTP (alternatif)", "kaynakların bu sunucudaki uygulama/API servisine erişimi kesilir", "bu sunucunun hedefe yaptığı uygulama/API çağrıları başarısız olur"),
        ["8443"] = new("HTTPS (alternatif)", "kaynakların bu sunucudaki uygulama/API servisine erişimi kesilir", "bu sunucunun hedefe yaptığı uygulama/API çağrıları başarısız olur"),
        ["9389"] = new("AD Web Services", "kaynaklar AD PowerShell modülü / AD Yönetim Merkezi ile bu DC üzerinden işlem yapamaz (kullanıcı açma, otomasyon scriptleri)", "bu sunucudaki AD PowerShell otomasyonları çalışmaz"),
        ["10050"] = new("Zabbix Agent", "Zabbix bu sunucuyu izleyemez; CPU/disk/servis alarmları gelmez", "bu sunucu Zabbix ajanlarından veri toplayamaz"),
        ["10051"] = new("Zabbix Server", "Zabbix ajanlarının gönderdiği izleme verileri ulaşmaz; alarmlar gelmez", "bu sunucunun izleme verileri Zabbix'e ulaşmaz; hakkında alarm üretilemez"),
    };

    static readonly PortInfo DynamicRpc = new("muhtemelen Windows dinamik RPC portu",
        "135 (RPC) üzerinden açılan Windows servis bağlantıları kesilir: WMI/DCOM ile uzaktan yönetim ve izleme, AD replikasyonu, uzaktan hizmet/görev yönetimi, DTC dağıtık işlemleri",
        "bu sunucunun hedefe 135 üzerinden açtığı RPC oturumları kurulamaz: WMI/DCOM ile yönetim-izleme, AD/domain işlemleri, DTC dağıtık işlemleri başarısız olur");

    public static PortInfo? Find(string? port, IConfiguration cfg)
    {
        if (string.IsNullOrWhiteSpace(port)) return null;
        port = port.Trim();
        // Kuruma özel açıklama (ör. "8443": "Kredi kartı provizyon API'si") her iki yönde de kullanılır.
        if (cfg[$"Ai:PortAciklamalari:{port}"] is { Length: > 0 } ozel)
            return new PortInfo(ozel, $"kaynaklar bu sunucudaki \"{ozel}\" işlevini kullanamaz", $"bu sunucu hedefteki \"{ozel}\" işlevini kullanamaz");
        if (Known.TryGetValue(port, out var p)) return p;
        return int.TryParse(port, out int n) && n >= 49152 ? DynamicRpc : null;
    }
}
