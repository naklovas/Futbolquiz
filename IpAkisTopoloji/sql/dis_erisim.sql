-- ---------------------------------------------------------------------------
-- İnternetten (dış ağdan) erişilen iç servisler — DisErisimServisi (Windows servisi) her gece yazar, DeltaFlow yalnız okur
-- (wwwroot/diserisim.html). Kaynaklar: Palo Alto firewall (Splunk) + Carbon Black (Splunk). Doku DB'de (ConnectionStrings:Envanter) çalıştırın.
-- Tablo adları servis ve DeltaFlow'da DisErisim:Tablolar:* ayarıyla değiştirilebilir.
-- ---------------------------------------------------------------------------

-- Servis (iç IP + port) başına kümülatif kayıt: ilk / son görülme (izinli erişim), son taramanın sayıları
IF OBJECT_ID('dbo.DF_DIS_ERISIM') IS NULL
CREATE TABLE dbo.DF_DIS_ERISIM (
    IP              VARCHAR(45)    NOT NULL,   -- iç IP (NAT sonrası; firewall'da dest_translated_ip, CB'de local_ip)
    PORT            INT            NOT NULL,
    ILK_GORULME     DATETIME2(0)   NULL,       -- internetten ilk izinli erişim (FW ya da CB)
    SON_GORULME     DATETIME2(0)   NULL,       -- internetten son izinli erişim
    FW_ILK          DATETIME2(0)   NULL,
    FW_SON          DATETIME2(0)   NULL,
    CB_ILK          DATETIME2(0)   NULL,
    CB_SON          DATETIME2(0)   NULL,
    TOPLAM_OTURUM   BIGINT         NOT NULL DEFAULT 0,   -- tüm taramalarda izinli oturum toplamı
    GUN_SAYISI      INT            NOT NULL DEFAULT 0,   -- erişim görülen tarama (gün) sayısı
    SON_OTURUM      BIGINT         NULL,       -- son taramada izinli oturum
    SON_DIS_IP      INT            NULL,       -- son taramada farklı dış IP
    SON_ENGEL       BIGINT         NULL,       -- son taramada engellenen oturum (firewall)
    SON_ENGEL_DIS_IP INT           NULL,
    DIS_HEDEF       NVARCHAR(400)  NULL,       -- firewall'daki NAT öncesi (public) hedef adres(ler)
    KURAL           NVARCHAR(400)  NULL,       -- firewall kural(lar)ı
    FW_UYGULAMA     NVARCHAR(200)  NULL,       -- Palo Alto App-ID
    EXE             NVARCHAR(600)  NULL,       -- Carbon Black: bağlantıyı kabul eden süreç(ler)
    SUNUCU          NVARCHAR(200)  NULL,       -- Carbon Black computer_name
    KAYNAK          VARCHAR(10)    NULL,       -- FW | CB | FW+CB (bugüne kadar)
    SON_TARAMA      DATETIME2(0)   NULL,       -- bu satırın en son güncellendiği tarama
    CONSTRAINT PK_DF_DIS_ERISIM PRIMARY KEY (IP, PORT)
);
GO

-- Günlük özet (izinli + engellenen): eğilim ve "en çok yoklanan servisler"
IF OBJECT_ID('dbo.DF_DIS_ERISIM_GUN') IS NULL
CREATE TABLE dbo.DF_DIS_ERISIM_GUN (
    TARIH           DATE           NOT NULL,   -- tarama aralığının bitiş günü
    IP              VARCHAR(45)    NOT NULL,
    PORT            INT            NOT NULL,
    IZIN_OTURUM     BIGINT         NOT NULL DEFAULT 0,
    IZIN_DIS_IP     INT            NOT NULL DEFAULT 0,
    ENGEL_OTURUM    BIGINT         NOT NULL DEFAULT 0,
    ENGEL_DIS_IP    INT            NOT NULL DEFAULT 0,
    FW              BIT            NOT NULL DEFAULT 0,
    CB              BIT            NOT NULL DEFAULT 0,
    ILK             DATETIME2(0)   NULL,       -- o gün ilk izinli erişim
    SON             DATETIME2(0)   NULL,
    CONSTRAINT PK_DF_DIS_ERISIM_GUN PRIMARY KEY (TARIH, IP, PORT)
);
GO

-- Ayrıntı: gün × servis × dış kaynak IP (yalnız izinli). Servis başına günde en çok DisErisim:MaxKaynakIp (0 = sınırsız) en çok oturumlu IP.
IF OBJECT_ID('dbo.DF_DIS_ERISIM_DETAY') IS NULL
CREATE TABLE dbo.DF_DIS_ERISIM_DETAY (
    TARIH           DATE           NOT NULL,
    IP              VARCHAR(45)    NOT NULL,
    PORT            INT            NOT NULL,
    DIS_IP          VARCHAR(45)    NOT NULL,
    KAYNAK          VARCHAR(10)    NOT NULL,   -- FW | CB | FW+CB
    OTURUM          BIGINT         NOT NULL,
    ILK             DATETIME2(0)   NULL,
    SON             DATETIME2(0)   NULL,
    CONSTRAINT PK_DF_DIS_ERISIM_DETAY PRIMARY KEY (TARIH, IP, PORT, DIS_IP)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DF_DIS_ERISIM_DETAY_DIS')
    CREATE INDEX IX_DF_DIS_ERISIM_DETAY_DIS ON dbo.DF_DIS_ERISIM_DETAY (DIS_IP, TARIH);
GO

-- Dış kaynak IP başına kümülatif: bu IP bu servise ilk / son ne zaman geldi
IF OBJECT_ID('dbo.DF_DIS_ERISIM_KAYNAK') IS NULL
CREATE TABLE dbo.DF_DIS_ERISIM_KAYNAK (
    IP              VARCHAR(45)    NOT NULL,
    PORT            INT            NOT NULL,
    DIS_IP          VARCHAR(45)    NOT NULL,
    ILK_GORULME     DATETIME2(0)   NULL,
    SON_GORULME     DATETIME2(0)   NULL,
    TOPLAM_OTURUM   BIGINT         NOT NULL DEFAULT 0,
    GUN_SAYISI      INT            NOT NULL DEFAULT 0,
    SON_TARIH       DATE           NULL,       -- en son işlendiği tarama günü (aynı gün yeniden taranırsa iki kez sayılmasın)
    CONSTRAINT PK_DF_DIS_ERISIM_KAYNAK PRIMARY KEY (IP, PORT, DIS_IP)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DF_DIS_ERISIM_KAYNAK_DIS')
    CREATE INDEX IX_DF_DIS_ERISIM_KAYNAK_DIS ON dbo.DF_DIS_ERISIM_KAYNAK (DIS_IP);
GO

-- Çalışma kaydı (rapor sayfasında "son tarama", hata ve uyarılar)
IF OBJECT_ID('dbo.DF_DIS_ERISIM_CALISMA') IS NULL
CREATE TABLE dbo.DF_DIS_ERISIM_CALISMA (
    ID              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DF_DIS_ERISIM_CALISMA PRIMARY KEY,
    BASLANGIC       DATETIME2(0)   NOT NULL,
    BITIS           DATETIME2(0)   NULL,
    ARALIK_BAS      DATETIME2(0)   NOT NULL,   -- taranan aralık (yerel saat)
    ARALIK_BIT      DATETIME2(0)   NOT NULL,
    DURUM           VARCHAR(20)    NOT NULL,   -- suruyor | tamam | hata
    SERVIS          INT            NULL,       -- izinli erişim görülen servis (IP + port)
    YENI_SERVIS     INT            NULL,       -- ilk kez görülen servis
    DETAY_SATIR     INT            NULL,
    KAYNAK_KESIK    INT            NULL,       -- kaynak IP sınırına takılan servis sayısı
    MESAJ           NVARCHAR(MAX)  NULL,
    MAKINE          NVARCHAR(100)  NULL
);
GO
