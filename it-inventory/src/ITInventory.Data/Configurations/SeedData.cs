namespace ITInventory.Data.Configurations;

/// <summary>
/// Nessus taramalarından gelen DeviceProfile değerlerinin (Ziraat_YD.DeviceProfile)
/// envanter kategorilerine ilk eşlemesi. Admin ekranından değiştirilebilir.
/// </summary>
internal static class SeedData
{
    public static readonly (int Id, string Name)[] Categories =
    {
        (1, "Server"),
        (2, "Network Device"),
        (3, "Security"),
        (4, "Audio/Video"),
        (5, "Storage"),
        (6, "Printer"),
        (7, "Client"),
        (8, "Virtualization"),
        (9, "Power/Infrastructure"),
        (10, "Other"),
    };

    /// <summary>
    /// ProfileName, Ziraat_YD.DeviceProfile ile birebir eşleşmesi gereken orijinal (kaynak) değerdir,
    /// değiştirilmemelidir. DisplayName sadece ekranda gösterilen İngilizce etikettir.
    /// </summary>
    public static readonly (string ProfileName, string DisplayName, int? CategoryId)[] DeviceProfiles =
    {
        ("Veri Depolama (NAS)", "Data Storage (NAS)", 5),
        ("Güvenlik Kameraları (CCTV / NVR)", "Security Cameras (CCTV / NVR)", 3),
        ("Sunucu / Appliance (Linux)", "Server / Appliance (Linux)", 1),
        ("IP Telefon (VoIP)", "IP Phone (VoIP)", 4),
        ("Kablosuz Ağ (Access Point)", "Wireless Network (Access Point)", 2),
        ("Ağ Cihazı (SAN Switch)", "Network Device (SAN Switch)", 2),
        ("Veri Depolama (Storage)", "Data Storage (Storage)", 5),
        ("Veri Depolama (Storage Server)", "Data Storage (Storage Server)", 5),
        ("Veri Depolama (Storage / NAS)", "Data Storage (Storage / NAS)", 5),
        ("Endüstriyel Ağ Geçidi (IoT)", "Industrial Gateway (IoT)", 2),
        ("Sunucu Yönetim Kartı (OOB / Console)", "Server Management Card (OOB / Console)", 1),
        ("Sanallaştırma (Container Host)", "Virtualization (Container Host)", 8),
        ("Yük Dengeleyici (Load Balancer)", "Load Balancer", 2),
        ("NULL", "Unmapped", null),
        ("Ağ Cihazı (Router)", "Network Device (Router)", 2),
        ("Yazıcı (Printer / Print Server)", "Printer (Printer / Print Server)", 6),
        ("Bütünleşik Sistem (HCI)", "Hyperconverged Infrastructure (HCI)", 1),
        ("Sunucu Yönetim (Console Server / OOB)", "Server Management (Console Server / OOB)", 1),
        ("Ağ Servisleri (SD-WAN)", "Network Services (SD-WAN)", 2),
        ("Sanallaştırma (Yönetim Sunucusu)", "Virtualization (Management Server)", 8),
        ("UPS / Güç Yönetimi", "UPS / Power Management", 9),
        ("Sunucu (Linux/SAP)", "Server (Linux/SAP)", 1),
        ("Sunucu (Linux)", "Server (Linux)", 1),
        ("Sunucu Yönetim (Console Server)", "Server Management (Console Server)", 1),
        ("Sunucu (Unix)", "Server (Unix)", 1),
        ("Güvenlik / Ağ Cihazı", "Security / Network Device", 3),
        ("Ağ Cihazı (Switch / Router)", "Network Device (Switch / Router)", 2),
        ("Güvenlik (Firewall)", "Security (Firewall)", 3),
        ("IP Telefon / Santral", "IP Phone / PBX", 4),
        ("Ağ Cihazı (Genel)", "Network Device (General)", 2),
        ("Ağ Cihazı (Switch)", "Network Device (Switch)", 2),
        ("Yazıcı (Printer)", "Printer", 6),
        ("Sunucu Yönetim Kartı (OOB)", "Server Management Card (OOB)", 1),
        ("Yazıcı (Print Server)", "Printer (Print Server)", 6),
        ("Sunucu / Appliance (Linux/IoT)", "Server / Appliance (Linux/IoT)", 1),
        ("Sanallaştırma (Hypervisor)", "Virtualization (Hypervisor)", 8),
        ("Güvenlik Kameraları (CCTV)", "Security Cameras (CCTV)", 3),
        ("Sunucu (Windows)", "Server (Windows)", 1),
        ("Medya Oynatıcı / Akıllı Ekran", "Media Player / Smart Display", 4),
        ("Ağ Servisleri (DDI)", "Network Services (DDI)", 2),
        ("İstemci Bilgisayar (Workstation)", "Client Computer (Workstation)", 7),
        ("IP Telefon (Analog Gateway)", "IP Phone (Analog Gateway)", 4),
        ("Güvenlik (Appliance)", "Security (Appliance)", 3),
        ("İstemci Bilgisayar (Workstation / Medya)", "Client Computer (Workstation / Media)", 7),
        ("Sunucu (Unix/Mainframe)", "Server (Unix/Mainframe)", 1),
        ("Ağ Cihazı (Switch / HCI)", "Network Device (Switch / HCI)", 2),
        ("Ağ Cihazı (Router / Firewall)", "Network Device (Router / Firewall)", 3),
    };
}
