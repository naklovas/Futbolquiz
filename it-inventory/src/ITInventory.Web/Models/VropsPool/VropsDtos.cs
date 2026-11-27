namespace ITInventory.Web.Models.VropsPool;

/// <summary>vROps'tan gelen bir ESXi host. Envanterde PhysicalDevice karsiligi var.</summary>
public class VropsHostDto
{
    public string VropsId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Bu host'un uzerinde kac VM calisiyor.</summary>
    public int VmCount { get; set; }

    // --- vROps'tan okunan alanlar. Hepsi BOS OLABILIR: her kurulum her
    //     alani doldurmuyor. Form on-doldurmada ve listede kullaniliyor.
    public string? IpAddress { get; set; }
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? Version { get; set; }
    public string? SerialNo { get; set; }

    /// <summary>Ayni adla bir PhysicalDevice zaten varsa onun Id'si.</summary>
    public int? ExistingPhysicalDeviceId { get; set; }

    public bool AlreadyInInventory => ExistingPhysicalDeviceId.HasValue;
}

/// <summary>vROps'tan gelen bir sanal makine. Envanterde Server karsiligi var.</summary>
public class VropsVmDto
{
    public string VropsId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Uzerinde calistigi ESXi host'un adi. Bugun elle doldurulan alan bu.</summary>
    public string HostName { get; set; } = string.Empty;

    public string HostVropsId { get; set; } = string.Empty;

    /// <summary>
    /// vSphere'in kendi kume servisi icin otomatik yarattigi VM (vCLS-...).
    /// Envantere girmemeli; listede isaretli gosterilip varsayilan olarak gizlenir.
    /// </summary>
    public bool IsSystemVm { get; set; }

    // --- vROps'tan okunan alanlar. Hepsi BOS OLABILIR.
    //     Konuk alanlari (guest|*) VMware Tools kurulu degilse hic gelmiyor;
    //     ornek dokumunde "none" degeri goruldu, onu da bos sayiyoruz.
    public string? IpAddress { get; set; }
    public string? OperatingSystem { get; set; }

    /// <summary>Konuk isletim sistemindeki makine adi. VM adindan farkli olabilir.</summary>
    public string? GuestHostName { get; set; }

    public string? PowerState { get; set; }

    /// <summary>Ayni adla bir Server zaten varsa onun Id'si.</summary>
    public int? ExistingServerId { get; set; }

    public bool AlreadyInInventory => ExistingServerId.HasValue;
}

/// <summary>Bir datacenter'in tek seferde cekilen icerigi.</summary>
public class VropsDatacenterContent
{
    public string DatacenterName { get; set; } = string.Empty;
    public List<VropsHostDto> Hosts { get; set; } = new();
    public List<VropsVmDto> Vms { get; set; } = new();

    /// <summary>vROps'a ulasilamadiysa sebebi. Doluysa ekran bunu gosterir.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Liste Vrops:DetailLimit degerinden buyuk oldugu icin IP / isletim
    /// sistemi / model alanlari cekilmedi. Ekran bunu kullaniciya yaziyor,
    /// yoksa alanlar "vROps'ta yok" sanilir.
    /// </summary>
    public bool DetailsSkipped { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Bir vROps kaynaginin HAM alanlari. "Envantere ekle" formunu hangi
/// property'den dolduracagimiza karar vermek icin kullaniliyor: property
/// adlari vROps surumune gore degisiyor, tahmin edilemiyor.
/// </summary>
public class VropsKaynakAlanlari
{
    public string VropsId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;

    /// <summary>property adi -> deger. vROps ne dondurduyse, kirpilmadan.</summary>
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>PARENT iliskileri: (tur, ad). VM icin host/cluster buradan cikiyor.</summary>
    public List<VropsIliskiliKaynak> Parents { get; set; } = new();

    public string? Error { get; set; }
}

public class VropsIliskiliKaynak
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// vROps'ta tanimli bir adaptor ornegi -- pratikte BIR vCENTER.
/// Ulke ayrimi datacenter adi yerine vCenter'a baglanacaksa esleme
/// bunlardan birine yapilacak, o yuzden gercek adlarini goruyoruz.
/// </summary>
public class VropsVcenter
{
    public string VropsId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AdapterKind { get; set; } = string.Empty;
    public string ResourceKind { get; set; } = string.Empty;
}

/// <summary>Alan kesfi ekraninin tamami: vCenter'lar, ornek VM'ler, ornek host'lar.</summary>
public class VropsAlanOrnekleri
{
    public List<VropsVcenter> Vcenters { get; set; } = new();
    public List<VropsKaynakAlanlari> Vms { get; set; } = new();
    public List<VropsKaynakAlanlari> Hosts { get; set; } = new();
    public string? Error { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
