using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Dünya ülkeleri referans listesi (Company.OriginCountry için) — bankanın kendi
/// operasyon ülkelerini temsil eden Country tablosundan bağımsızdır, admin tarafından
/// yönetilir (ekle/düzenle/pasifleştir).
/// </summary>
public class OriginCountry : AuditableEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<Company> Companies { get; set; } = new List<Company>();
}
