-- DeltaFlow · Fiziksel cihaz değerleri (Varlık, İş, Erişilebilirlik, Gizlilik, Bütünlük): denetim kayıtları ve elle değerler.
-- DeltaFlow nlyte / doku tablolarına YAZMAZ; yalnız bu DF_ tablolarına yazar. Tablo adları Fiziksel:DegerTablolari ile değiştirilebilir.
-- Değerler 0–5; NULL = o boyut için değer yok / anlamsız (ör. PDU'nun gizlilik değeri).

-- 1) Kayıt başlığı: "Değerleri kaydet" her basıldığında bir satır (denetim dönemi kanıtı)
IF OBJECT_ID('dbo.DF_FIZIKSEL_DEGER_KAYIT', 'U') IS NULL
CREATE TABLE dbo.DF_FIZIKSEL_DEGER_KAYIT (
    KAYIT_ID     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DF_FIZIKSEL_DEGER_KAYIT PRIMARY KEY,
    TARIH        DATETIME2      NOT NULL CONSTRAINT DF_DF_FIZIKSEL_DEGER_KAYIT_TARIH DEFAULT SYSDATETIME(),
    KAYDEDEN     NVARCHAR(100)  NULL,
    ACIKLAMA     NVARCHAR(500)  NULL,
    CIHAZ_SAYISI INT            NULL,
    KABIN_SAYISI INT            NULL,
    VERI_TARIHI  DATETIME2      NULL,      -- nlyte verisinin son yenilenme zamanı (sp_KabinZincirYenile)
    KURALLAR     NVARCHAR(MAX)  NULL       -- o günkü değer kuralları (JSON)
);
GO

-- 2) Cihaz değerleri (kayıt başına tüm aktif kabin cihazları)
IF OBJECT_ID('dbo.DF_FIZIKSEL_DEGER', 'U') IS NULL
CREATE TABLE dbo.DF_FIZIKSEL_DEGER (
    KAYIT_ID        INT            NOT NULL,
    KABIN           NVARCHAR(200)  NULL,
    ODA             NVARCHAR(50)   NULL,
    CIHAZ           NVARCHAR(300)  NULL,
    ASSETID         NVARCHAR(50)   NULL,
    TUR             NVARCHAR(50)   NULL,
    MODEL           NVARCHAR(200)  NULL,
    VARLIK          TINYINT        NULL,
    IS_DEGERI       TINYINT        NULL,
    ERISILEBILIRLIK TINYINT        NULL,
    GIZLILIK        TINYINT        NULL,
    BUTUNLUK        TINYINT        NULL,
    YONTEM          NVARCHAR(500)  NULL,   -- VM'lerin uygulamalarından / ERT'deki uygulamalarından / kural: … / elle değer …
    KAYNAK          NVARCHAR(1000) NULL,   -- en yüksek değeri veren uygulama / kural / cihaz
    KURAL           NVARCHAR(200)  NULL,
    ELLE            BIT            NOT NULL CONSTRAINT DF_DF_FIZIKSEL_DEGER_ELLE DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DF_FIZIKSEL_DEGER_KAYIT')
    CREATE INDEX IX_DF_FIZIKSEL_DEGER_KAYIT ON dbo.DF_FIZIKSEL_DEGER (KAYIT_ID, KABIN);
GO

-- 3) Kabin değerleri (kayıt başına)
IF OBJECT_ID('dbo.DF_FIZIKSEL_DEGER_KABIN', 'U') IS NULL
CREATE TABLE dbo.DF_FIZIKSEL_DEGER_KABIN (
    KAYIT_ID        INT            NOT NULL,
    KABIN           NVARCHAR(200)  NOT NULL,
    ODA             NVARCHAR(50)   NULL,
    VARLIK          TINYINT        NULL,
    IS_DEGERI       TINYINT        NULL,
    ERISILEBILIRLIK TINYINT        NULL,
    GIZLILIK        TINYINT        NULL,
    BUTUNLUK        TINYINT        NULL,
    KAYNAK          NVARCHAR(1000) NULL,
    CIHAZ_SAYISI    INT            NULL,
    VM_SAYISI       INT            NULL,
    CONSTRAINT PK_DF_FIZIKSEL_DEGER_KABIN PRIMARY KEY (KAYIT_ID, KABIN)
);
GO

-- 4) Elle değerler: hesaplanan / kural değerinin yerine geçer (boş bırakılan boyut hesaplanandan gelir). Gerekçe zorunlu.
IF OBJECT_ID('dbo.DF_FIZIKSEL_DEGER_ELLE', 'U') IS NULL
CREATE TABLE dbo.DF_FIZIKSEL_DEGER_ELLE (
    ASSETID         NVARCHAR(50)   NOT NULL CONSTRAINT PK_DF_FIZIKSEL_DEGER_ELLE PRIMARY KEY,   -- nlyte AssetID
    CIHAZ           NVARCHAR(300)  NULL,
    KABIN           NVARCHAR(200)  NULL,
    VARLIK          TINYINT        NULL,
    IS_DEGERI       TINYINT        NULL,
    ERISILEBILIRLIK TINYINT        NULL,
    GIZLILIK        TINYINT        NULL,
    BUTUNLUK        TINYINT        NULL,
    GEREKCE         NVARCHAR(1000) NOT NULL,
    KAYDEDEN        NVARCHAR(100)  NULL,
    TARIH           DATETIME2      NOT NULL CONSTRAINT DF_DF_FIZIKSEL_DEGER_ELLE_TARIH DEFAULT SYSDATETIME()
);
GO

-- DeltaFlow servis hesabına yetki (hesap adını değiştirin):
-- GRANT SELECT, INSERT ON dbo.DF_FIZIKSEL_DEGER_KAYIT  TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT, INSERT ON dbo.DF_FIZIKSEL_DEGER        TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT, INSERT ON dbo.DF_FIZIKSEL_DEGER_KABIN  TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DF_FIZIKSEL_DEGER_ELLE TO [DOMAIN\DeltaFlowServisHesabi];

-- Denetim örnekleri:
-- Son kayıt:            SELECT TOP 1 * FROM dbo.DF_FIZIKSEL_DEGER_KAYIT ORDER BY KAYIT_ID DESC;
-- Bir dönemin kabinleri: SELECT k.TARIH, d.* FROM dbo.DF_FIZIKSEL_DEGER_KABIN d JOIN dbo.DF_FIZIKSEL_DEGER_KAYIT k ON k.KAYIT_ID = d.KAYIT_ID
--                        WHERE k.KAYIT_ID = (SELECT MAX(KAYIT_ID) FROM dbo.DF_FIZIKSEL_DEGER_KAYIT WHERE TARIH < '2027-01-01') ORDER BY d.KABIN;
-- Elle değiştirilenler:  SELECT * FROM dbo.DF_FIZIKSEL_DEGER_ELLE ORDER BY TARIH DESC;
