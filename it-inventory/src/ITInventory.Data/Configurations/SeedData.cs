namespace ITInventory.Data.Configurations;

/// <summary>
/// Nessus taramalarından gelen DeviceProfile değerlerinin (Ziraat_YD.DeviceProfile)
/// envanter kategorilerine ilk eşlemesi. Admin ekranından değiştirilebilir.
/// </summary>
internal static class SeedData
{
    public static readonly (int Id, string Name)[] Categories =
    {
        (1, "Sunucu"),
        (2, "Ağ Cihazı"),
        (3, "Güvenlik"),
        (4, "Ses/Görüntü"),
        (5, "Depolama"),
        (6, "Yazıcı"),
        (7, "İstemci"),
        (8, "Sanallaştırma"),
        (9, "Güç/Altyapı"),
        (10, "Diğer"),
    };

    public static readonly (string ProfileName, int? CategoryId)[] DeviceProfiles =
    {
        ("Veri Depolama (NAS)", 5),
        ("Güvenlik Kameraları (CCTV / NVR)", 3),
        ("Sunucu / Appliance (Linux)", 1),
        ("IP Telefon (VoIP)", 4),
        ("Kablosuz Ağ (Access Point)", 2),
        ("Ağ Cihazı (SAN Switch)", 2),
        ("Veri Depolama (Storage)", 5),
        ("Veri Depolama (Storage Server)", 5),
        ("Veri Depolama (Storage / NAS)", 5),
        ("Endüstriyel Ağ Geçidi (IoT)", 2),
        ("Sunucu Yönetim Kartı (OOB / Console)", 1),
        ("Sanallaştırma (Container Host)", 8),
        ("Yük Dengeleyici (Load Balancer)", 2),
        ("NULL", null),
        ("Ağ Cihazı (Router)", 2),
        ("Yazıcı (Printer / Print Server)", 6),
        ("Bütünleşik Sistem (HCI)", 1),
        ("Sunucu Yönetim (Console Server / OOB)", 1),
        ("Ağ Servisleri (SD-WAN)", 2),
        ("Sanallaştırma (Yönetim Sunucusu)", 8),
        ("UPS / Güç Yönetimi", 9),
        ("Sunucu (Linux/SAP)", 1),
        ("Sunucu (Linux)", 1),
        ("Sunucu Yönetim (Console Server)", 1),
        ("Sunucu (Unix)", 1),
        ("Güvenlik / Ağ Cihazı", 3),
        ("Ağ Cihazı (Switch / Router)", 2),
        ("Güvenlik (Firewall)", 3),
        ("IP Telefon / Santral", 4),
        ("Ağ Cihazı (Genel)", 2),
        ("Ağ Cihazı (Switch)", 2),
        ("Yazıcı (Printer)", 6),
        ("Sunucu Yönetim Kartı (OOB)", 1),
        ("Yazıcı (Print Server)", 6),
        ("Sunucu / Appliance (Linux/IoT)", 1),
        ("Sanallaştırma (Hypervisor)", 8),
        ("Güvenlik Kameraları (CCTV)", 3),
        ("Sunucu (Windows)", 1),
        ("Medya Oynatıcı / Akıllı Ekran", 4),
        ("Ağ Servisleri (DDI)", 2),
        ("İstemci Bilgisayar (Workstation)", 7),
        ("IP Telefon (Analog Gateway)", 4),
        ("Güvenlik (Appliance)", 3),
        ("İstemci Bilgisayar (Workstation / Medya)", 7),
        ("Sunucu (Unix/Mainframe)", 1),
        ("Ağ Cihazı (Switch / HCI)", 2),
        ("Ağ Cihazı (Router / Firewall)", 3),
    };
}
