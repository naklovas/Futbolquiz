namespace ITInventory.Web.Configuration;

public class TestLoginSettings
{
    public const string SectionName = "TestLogin";

    /// <summary>Defaults to enabled so the feature works even without an appsettings.json entry.</summary>
    public bool Enabled { get; set; } = true;

    public string Password { get; set; } = "12345";
}
