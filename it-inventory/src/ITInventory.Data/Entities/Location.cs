using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Bir ülkeye ait şube/lokasyon referans kaydı (ör. Almanya -> Berlin). Admin tarafından
/// yönetilir; envanter formlarındaki serbest metin "Branch" alanı için otomatik
/// öneri/liste kaynağı olarak kullanılır.
/// </summary>
public class Location : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string Branch { get; set; } = string.Empty;

    /// <summary>Sınıf/kategori (ör. "Yurtdışı Şube", "Yurtdışı İştirak").</summary>
    public string Class { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
