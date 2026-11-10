-- DeltaFlow · HTTP.sys URL kayıtları: hangi port / URL hangi süreçte (exe) çalışıyor.
-- Carbon Black HTTP.sys portlarının sahibini ntoskrnl.exe / System görür; asıl uygulama bu tablodan bulunur.
-- Tabloyu araclar/HttpSysTopla.ps1 doldurur (sunucularda `netsh http show servicestate view=requestq verbose=yes`).
-- DeltaFlow yalnız OKUR (HttpSys:Tablo ile ad değiştirilebilir). Betik her sunucu için önce o sunucunun satırlarını siler, sonra yazar.

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
