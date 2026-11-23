namespace ITInventory.Web.Models.VropsPool;

/// <summary>vROps'tan gelen bir ESXi host. Envanterde PhysicalDevice karsiligi var.</summary>
public class VropsHostDto
{
    public string VropsId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Bu host'un uzerinde kac VM calisiyor.</summary>
    public int VmCount { get; set; }

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

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
