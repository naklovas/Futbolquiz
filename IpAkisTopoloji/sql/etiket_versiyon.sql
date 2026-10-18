-- DeltaFlow · Etiket açıklamaları ve topoloji versiyonları (doku DB'si, ConnectionStrings:Envanter).
-- Alt etiketler ayrı tablo istemez: ERT_HOSTIPADDRESS.TAG kolonunda "ANA/ALT" (ör. MOBILBANKACILIK/MOBIL APP) olarak durur.

-- Ana ve alt etiket açıklamaları (Etiket:AciklamaTablosu). ETIKET = "MOBILBANKACILIK" ya da "MOBILBANKACILIK/MOBIL APP"
IF OBJECT_ID('dbo.DF_ETIKET_ACIKLAMA', 'U') IS NULL
CREATE TABLE dbo.DF_ETIKET_ACIKLAMA (
    ETIKET      NVARCHAR(120)  NOT NULL PRIMARY KEY,
    ACIKLAMA    NVARCHAR(2000) NULL,
    GUNCELLEYEN NVARCHAR(200)  NULL,
    TARIH       DATETIME2      NOT NULL DEFAULT SYSDATETIME()
);
GO

-- İşlev haritası + küme topolojisi versiyonları (Islev:VersiyonTablosu); etiket başına son 3 (Islev:VersiyonSayisi) tutulur.
IF OBJECT_ID('dbo.DF_TOPOLOJI_VERSIYON', 'U') IS NULL
CREATE TABLE dbo.DF_TOPOLOJI_VERSIYON (
    ID        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ETIKET    NVARCHAR(120)  NOT NULL,
    KURUM     NVARCHAR(1000) NULL,      -- seçili kurumlar (boş = tümü)
    ACIKLAMA  NVARCHAR(500)  NULL,
    PARAMETRE NVARCHAR(1000) NULL,      -- JSON: tarih aralığı, kaynaklar
    KAYDEDEN  NVARCHAR(200)  NULL,
    TARIH     DATETIME2      NOT NULL DEFAULT SYSDATETIME(),
    SUNUCU    INT            NULL,
    KUME      INT            NULL,
    HARITA    NVARCHAR(MAX)  NOT NULL,  -- api/islev yanıtı (kümeleme)
    TOPOLOJI  NVARCHAR(MAX)  NOT NULL   -- api/islev/topoloji yanıtı
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DF_TOPOLOJI_VERSIYON_ETIKET')
    CREATE INDEX IX_DF_TOPOLOJI_VERSIYON_ETIKET ON dbo.DF_TOPOLOJI_VERSIYON (ETIKET, TARIH DESC);
GO

-- Alt etiketlerle TAG metni uzayabilir (ör. "MOBILBANKACILIK/MOBIL APP,VERIAMBARI"); 200 yetmezse:
-- ALTER TABLE dbo.ERT_HOSTIPADDRESS ALTER COLUMN TAG NVARCHAR(400) NULL;

-- Uygulama havuzu hesabına yetki (gerekirse):
-- GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DF_ETIKET_ACIKLAMA TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT, INSERT, DELETE ON dbo.DF_TOPOLOJI_VERSIYON TO [DOMAIN\DeltaFlowServisHesabi];
