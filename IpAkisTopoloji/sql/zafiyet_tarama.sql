-- Açıklık taraması (Zafiyet.cs): biten taramaların sonucu ("Kayıtlıdan getir"). DeltaFlow yalnız bu tabloya yazar.
-- Ayar: Zafiyet:TaramaTablosu (varsayılan dbo.DF_ZAFIYET_TARAMA), Zafiyet:KayitSayisi (kural başına tutulan son kayıt, varsayılan 5).
-- Tablo yoksa sonuçlar yalnız bellekte tutulur (uygulama yeniden başlayınca kaybolur).
IF OBJECT_ID('dbo.DF_ZAFIYET_TARAMA') IS NULL
BEGIN
    CREATE TABLE dbo.DF_ZAFIYET_TARAMA (
        ID        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        KURAL_ID  NVARCHAR(100) NOT NULL,          -- kural Id'si (anlık kuralda "anlik:<cve ya da ad>")
        KURAL_AD  NVARCHAR(400) NULL,
        CVE       NVARCHAR(400) NULL,
        KAPSAM    NVARCHAR(400) NULL,              -- "tüm sunucular" ya da listeye eklenen IP'ler
        SAAT      INT           NULL,              -- Carbon Black ağ olayı aralığı (saat)
        SATIR     INT           NOT NULL,
        OZET      NVARCHAR(400) NULL,              -- JSON: öncelik → satır sayısı
        SONUC     VARBINARY(MAX) NOT NULL,         -- gzip JSON {is, satirlar, mesajlar}
        TARIH     DATETIME2     NOT NULL DEFAULT SYSDATETIME(),
        KAYDEDEN  NVARCHAR(200) NULL
    );
    CREATE INDEX IX_DF_ZAFIYET_TARAMA_KURAL ON dbo.DF_ZAFIYET_TARAMA (KURAL_ID, TARIH DESC);
END
