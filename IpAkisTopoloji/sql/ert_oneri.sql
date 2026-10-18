-- DeltaFlow · ERT eksikleri: APPNAME'i boş (IP, port) satırları için AI uygulama önerileri ve kararlar.
-- ERT_HOSTIPADDRESS'e YAZILMAZ; onaylananlar sayfadan Excel'e (CSV) aktarılıp ERT'yi besleyen ekibe iletilir.
IF OBJECT_ID('dbo.DF_ERT_UYGULAMA_ONERI', 'U') IS NULL
CREATE TABLE dbo.DF_ERT_UYGULAMA_ONERI (
    IP       NVARCHAR(45)   NOT NULL,
    PORT     NVARCHAR(20)   NOT NULL,      -- ERT'de port boşsa ''
    UYGULAMA NVARCHAR(200)  NULL,          -- önerilen / onaylanan uygulama adı
    YENI     BIT            NOT NULL DEFAULT 0,   -- ERT'deki uygulama listesinde olmayan yeni ad
    GUVEN    INT            NULL,
    GEREKCE  NVARCHAR(1000) NULL,
    DURUM    NVARCHAR(20)   NOT NULL,      -- AI / Onaylandı / Reddedildi / Elle
    KAYNAK   NVARCHAR(200)  NULL,          -- "AI" ya da kararı veren kullanıcı
    TARIH    DATETIME2      NOT NULL DEFAULT SYSDATETIME(),
    KANIT    NVARCHAR(MAX)  NULL,          -- AI'a giden kanıt metni (sunucu başına)
    CONSTRAINT PK_DF_ERT_UYGULAMA_ONERI PRIMARY KEY (IP, PORT)
);
GO
-- GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DF_ERT_UYGULAMA_ONERI TO [DOMAIN\DeltaFlowServisHesabi];
