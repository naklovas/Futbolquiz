namespace ITInventory.Data.Entities;

/// <summary>
/// One logical topology diagram per country (CountryId is both PK and FK -- a new upload
/// replaces the existing row rather than accumulating history). Stored in the DB rather than
/// on disk so it doesn't depend on a shared file path across app servers/deployments.
/// </summary>
public class CountryTopologyFile
{
    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] FileData { get; set; } = Array.Empty<byte>();
    public long FileSize { get; set; }

    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
}
