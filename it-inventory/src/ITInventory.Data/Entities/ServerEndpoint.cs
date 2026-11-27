using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Bir sunucunun IP:PORT üzerinden barındırdığı tek bir uygulama eşlemesi. Bir Server'ın
/// birden fazla ServerEndpoint'i olabilir (aynı makinede farklı portlarda farklı uygulamalar).
/// </summary>
public class ServerEndpoint : AuditableEntity
{
    public int Id { get; set; }

    public int ServerId { get; set; }
    public Server? Server { get; set; }

    public string? IpAddress { get; set; }
    public int? Port { get; set; }

    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }

    public string? Notes { get; set; }
}
