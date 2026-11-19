-- Sunucu işleten birim önerileri (Birim.cs): ERT'de SUNUCUISLETENBIRIM boş / ERT'de kaydı olmayan sunucular için AI / kural önerisi. ERT'ye yazılmaz.
-- Ayar: Birim:OneriTablosu (varsayılan dbo.DF_BIRIM_ONERI). Tablo yoksa öneriler yalnız bellekte tutulur.
IF OBJECT_ID('dbo.DF_BIRIM_ONERI') IS NULL
CREATE TABLE dbo.DF_BIRIM_ONERI (
    IP        VARCHAR(45)    NOT NULL PRIMARY KEY,
    BIRIM     NVARCHAR(400)  NULL,            -- önerilen işleten birim (ERT'deki birim adlarından biri) ya da NULL (bulunamadı)
    GUVEN     INT            NOT NULL DEFAULT 0,
    GEREKCE   NVARCHAR(1000) NULL,
    KAYNAK    NVARCHAR(20)   NOT NULL,        -- AI | Kural | Elle
    DURUM     NVARCHAR(20)   NOT NULL,        -- AI | Onaylandı | Reddedildi | Elle
    TARIH     DATETIME2      NOT NULL DEFAULT SYSDATETIME(),
    KAYDEDEN  NVARCHAR(200)  NULL
);
