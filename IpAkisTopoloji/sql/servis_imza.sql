-- DeltaFlow servis imzası taraması (Imza.cs, imza.html): tüm ERT IP + port → Carbon Black exe + IIS uygulamaları.
-- Ham tarama gzip'li JSON olarak tek satırda (AD = 'TUM'); uzun taramada 10 grupta bir ara kayıt (TAMAM = 0 → "kaldığı yerden devam").
-- DeltaFlow yalnız bu tabloya yazar; ERT'ye yazılmaz.
IF OBJECT_ID('dbo.DF_SERVIS_IMZA') IS NULL
CREATE TABLE dbo.DF_SERVIS_IMZA (
    AD        NVARCHAR(50)   NOT NULL PRIMARY KEY,
    SONUC     VARBINARY(MAX) NOT NULL,
    SUNUCU    INT            NOT NULL,
    TAMAM     BIT            NOT NULL,
    TARIH     DATETIME2      NOT NULL,
    KAYDEDEN  NVARCHAR(200)  NULL
);
