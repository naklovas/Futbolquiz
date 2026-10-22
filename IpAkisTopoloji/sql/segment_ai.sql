-- DeltaFlow · Segment / etiket tarama: sunucu başına AI iş uygulaması tahmini (Carbon Black exe'lerinden).
-- Kullanıcı: "tekrar tekrar tahmin etmeyeyim" → sonuç kaydedilir; sonraki taramada sunucunun iş exe'leri kayıttakilerin içindeyse
-- AI'a yeniden sorulmaz, yeni exe görülürse yeniden tahmin edilir. Yoksa sonuçlar yalnız bellekte tutulur (uygulama yeniden başlayınca gider).
IF OBJECT_ID('dbo.DF_SEGMENT_AI_TAHMIN', 'U') IS NULL
CREATE TABLE dbo.DF_SEGMENT_AI_TAHMIN (
    IP       NVARCHAR(45)   NOT NULL PRIMARY KEY,
    SONUC    NVARCHAR(MAX)  NOT NULL,      -- JSON: { ozet, uygulamalar: [ { exe, yol, uygulama, rol, ertKarsiligi, guven, gerekce } ] }
    EXELER   NVARCHAR(MAX)  NULL,          -- tahminde AI'a giden iş exe yolları (| ile), yeni exe kontrolü için
    TARIH    DATETIME2      NOT NULL DEFAULT SYSDATETIME(),
    KAYDEDEN NVARCHAR(200)  NULL
);
GO
-- GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DF_SEGMENT_AI_TAHMIN TO [DOMAIN\DeltaFlowServisHesabi];
