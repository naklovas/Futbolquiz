namespace ITInventory.Web.Configuration;

public class LdapSettings
{
    public const string SectionName = "Ldap";

    /// <summary>Örn: fintek.local</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Domain controller adları/IP'leri. Boşsa Domain adı DNS üzerinden çözülür.</summary>
    public string[] Servers { get; set; } = Array.Empty<string>();

    public int Port { get; set; } = 636;
    public bool UseSsl { get; set; } = true;
}
