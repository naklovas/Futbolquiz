-- DeltaFlow · Segment / etiket tarama sonuçlarının kaydı (kullanıcı: "her seferinde tekrar sorgulamayayım, kaydetsin;
-- istersem yeniden çek, ya da yeni sunucuları ekle"). Tür + ad başına SON tarama tutulur; sonuç gzip'li JSON (VARBINARY).
-- Tablo yoksa taramalar yalnız bellekte tutulur (uygulama yeniden başlayınca gider).
IF OBJECT_ID('dbo.DF_SEGMENT_TARAMA', 'U') IS NULL
CREATE TABLE dbo.DF_SEGMENT_TARAMA (
    TUR      NVARCHAR(20)    NOT NULL,     -- segment | etiket
    AD       NVARCHAR(400)   NOT NULL,     -- segment adı / CIDR ya da etiket
    SONUC    VARBINARY(MAX)  NOT NULL,     -- gzip JSON: sunucular, satırlar (port / exe), giden, Nessus
    SAAT     INT             NOT NULL,     -- Carbon Black aralığı (saat)
    SUNUCU   INT             NOT NULL,
    SATIR    INT             NOT NULL,
    TARIH    DATETIME2       NOT NULL DEFAULT SYSDATETIME(),
    KAYDEDEN NVARCHAR(200)   NULL,
    CONSTRAINT PK_DF_SEGMENT_TARAMA PRIMARY KEY (TUR, AD)
);
GO
-- GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DF_SEGMENT_TARAMA TO [DOMAIN\DeltaFlowServisHesabi];
