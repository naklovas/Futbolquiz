-- DeltaFlow · HTTP.sys portu (ntoskrnl / System) → asıl exe kayıtları. DeltaFlow SUNUCULARA BAĞLANMAZ.
-- 1) DF_HTTPSYS_KORELASYON: Carbon Black zaman korelasyonu sonuçları — DeltaFlow yazar ve önce buna bakar (HttpSys:YenilemeSaat = 24 saatten eskiyse yeniden hesaplar).
IF OBJECT_ID('dbo.DF_HTTPSYS_KORELASYON', 'U') IS NULL
CREATE TABLE dbo.DF_HTTPSYS_KORELASYON (
    IP            VARCHAR(45)     NOT NULL,
    PORT          VARCHAR(10)     NOT NULL,   -- '0' = sunucuda ntoskrnl portu görülmedi işaret satırı
    SIRA          INT             NOT NULL,   -- 1 = en olası aday; 0 = aday yok
    EXE           NVARCHAR(260)   NULL,
    EXE_YOL       NVARCHAR(1000)  NULL,
    R             FLOAT           NULL,       -- korelasyon (Pearson, dakika dakika)
    ESLIK         INT             NULL,       -- gelen trafiğin olduğu dakikaların yüzde kaçında exe de dışarı bağlanıyor
    ORTAK_DAKIKA  INT             NULL,
    DAKIKA        INT             NULL,       -- portun gelen trafik gördüğü dakika
    BAGLANTI      BIGINT          NULL,
    TARIH         DATETIME2       NOT NULL CONSTRAINT DF_DF_HTTPSYS_KORELASYON_TARIH DEFAULT SYSDATETIME()
);
GO
-- Sonradan eklendi: aynı istemci + dakikada exe'nin "gelen port 0" olayı sayısı (en güçlü eşleşme)
IF COL_LENGTH('dbo.DF_HTTPSYS_KORELASYON', 'ISTEMCI') IS NULL
    ALTER TABLE dbo.DF_HTTPSYS_KORELASYON ADD ISTEMCI INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DF_HTTPSYS_KORELASYON_IP' AND object_id = OBJECT_ID('dbo.DF_HTTPSYS_KORELASYON'))
    CREATE INDEX IX_DF_HTTPSYS_KORELASYON_IP ON dbo.DF_HTTPSYS_KORELASYON (IP, PORT);
GO
-- GRANT SELECT, INSERT, DELETE ON dbo.DF_HTTPSYS_KORELASYON TO [DOMAIN\deltaflow_hesabi];

-- 2) DF_HTTPSYS_URL (İSTEĞE BAĞLI): birisi araclar/HttpSysTopla.ps1 ile sunucularda netsh çalıştırıp kesin kayıt toplarsa; DeltaFlow yalnız OKUR.
IF OBJECT_ID('dbo.DF_HTTPSYS_URL', 'U') IS NULL
CREATE TABLE dbo.DF_HTTPSYS_URL (
    IP             VARCHAR(45)     NOT NULL,   -- sunucunun IPv4'ü (URL tüm IP'lerdeyse (+ / *) her IP için bir satır)
    HOSTNAME       NVARCHAR(200)   NOT NULL,   -- betiğin çalıştığı sunucu adı
    PORT           INT             NOT NULL,
    PROTOKOL       VARCHAR(10)     NULL,       -- http / https
    URL            NVARCHAR(1000)  NOT NULL,   -- kayıtlı URL (ör. https://+:8031/api/)
    KUYRUK         NVARCHAR(400)   NULL,       -- istek kuyruğu adı (IIS'te uygulama havuzu)
    PID            INT             NULL,       -- kuyruğa bağlı süreç (IIS havuzu boştaysa NULL)
    EXE_YOL        NVARCHAR(1000)  NULL,
    KOMUT_SATIRI   NVARCHAR(2000)  NULL,
    TOPLAMA_TARIHI DATETIME2       NOT NULL CONSTRAINT DF_DF_HTTPSYS_URL_TARIH DEFAULT SYSDATETIME(),
    TOPLAYAN       NVARCHAR(200)   NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DF_HTTPSYS_URL_IP' AND object_id = OBJECT_ID('dbo.DF_HTTPSYS_URL'))
    CREATE INDEX IX_DF_HTTPSYS_URL_IP ON dbo.DF_HTTPSYS_URL (IP, PORT);
GO
-- Yetkiler (örnek): DeltaFlow uygulama havuzu hesabı yalnız okur, betiği çalıştıran hesap yazar
-- GRANT SELECT ON dbo.DF_HTTPSYS_URL TO [DOMAIN\deltaflow_hesabi];
-- GRANT SELECT, INSERT, DELETE ON dbo.DF_HTTPSYS_URL TO [DOMAIN\toplama_hesabi];
