-- DeltaFlow · ERT'de olmayan sunucuların etiketleri (doku DB'si, ConnectionStrings:Envanter; Etiket:EkTablosu).
-- ERT_HOSTIPADDRESS'te satırı olmayan (CMDB'nin taramadığı, segment taramasında görülen) IP'ler. ERT'ye satır eklenmez.
-- TAG biçimi ERT TAG kolonuyla aynı: virgülle birden çok, alt etiket "ANA/ALT". IP sonradan ERT'ye girerse ERT TAG'i geçerli olur.
IF OBJECT_ID('dbo.DF_ETIKET_EK', 'U') IS NULL
CREATE TABLE dbo.DF_ETIKET_EK (
    IPADDRESS   NVARCHAR(45)  NOT NULL PRIMARY KEY,
    TAG         NVARCHAR(200) NULL,
    HOSTNAME    NVARCHAR(200) NULL,
    GUNCELLEYEN NVARCHAR(200) NULL,
    TARIH       DATETIME2     NOT NULL DEFAULT SYSDATETIME()
);
GO
