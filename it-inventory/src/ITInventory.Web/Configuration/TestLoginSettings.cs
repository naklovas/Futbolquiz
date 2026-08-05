namespace ITInventory.Web.Configuration;

public class TestLoginSettings
{
    public const string SectionName = "TestLogin";

    public bool Enabled { get; set; }
    public string Password { get; set; } = string.Empty;
}
