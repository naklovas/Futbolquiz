namespace ITInventory.Web.Configuration;

public class TestLoginSettings
{
    public const string SectionName = "TestLogin";

    /// <summary>
    /// Defaults to disabled -- a missing/deleted config entry should fail closed, not silently
    /// re-enable the LDAP bypass. The password itself lives in dbo.TestLoginConfig, not here.
    /// </summary>
    public bool Enabled { get; set; }
}
