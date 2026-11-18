-- Açıklık taraması (Zafiyet.cs): sayfadan kaydedilen kurallar (CVE / süreç adları / sürüm aralıkları / önlem). DeltaFlow yalnız bu tabloya yazar.
-- Ayar: Zafiyet:KuralTablosu (varsayılan dbo.DF_ZAFIYET_KURAL). Tablo yoksa kurallar yalnız bellekte tutulur.
IF OBJECT_ID('dbo.DF_ZAFIYET_KURAL') IS NULL
CREATE TABLE dbo.DF_ZAFIYET_KURAL (
    ID        NVARCHAR(100) NOT NULL PRIMARY KEY,   -- CVE ya da addan türetilen kısa ad
    KURAL     NVARCHAR(MAX) NOT NULL,               -- JSON (ad, cve, exeler, araliklar, onlem…)
    TARIH     DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
    KAYDEDEN  NVARCHAR(200) NULL
);
