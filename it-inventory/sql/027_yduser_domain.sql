-- 027_yduser_domain.sql
--
-- dbo.YDUsers tablosuna Domain kolonu ekler ve benzersizlik kisitini
-- (Username) yerine (Username, Domain) ciftine tasir.
--
-- NEDEN:
-- Kullanicilar artik tek domainden gelmiyor (fintek.local ve ZFGISTIRAK
-- birlikte). Giris akisi kullanici adindaki domain kismini atip yalnizca
-- Username ile ariyordu; ali@fintek.local ile ali@ZFGISTIRAK AYNI envanter
-- kaydina, dolayisiyla ayni ulke yetkisine dusuyordu. Bu bir yetki aciydi.
--
-- Domain'i NULL olan MEVCUT kayitlar varsayilan domaine (appsettings
-- icindeki Ldap domain listesinin ilki) ait sayilir; boylece bugun giris
-- yapabilen kimse etkilenmiyor.
--
-- Idempotenttir.

SET NOCOUNT ON;

-- ------------------------------------------------------------ 1) kolon
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.YDUsers') AND name = 'Domain')
BEGIN
    ALTER TABLE dbo.YDUsers ADD [Domain] nvarchar(100) NULL;
    PRINT 'Domain kolonu eklendi.';
END
ELSE
    PRINT 'Domain kolonu zaten var.';
GO

-- ------------------------------------------------------------ 2) indeks
-- Eski tekil indeksi birak, yerine ciftli olani kur.
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'IX_YDUsers_Username' AND object_id = OBJECT_ID('dbo.YDUsers'))
BEGIN
    DROP INDEX [IX_YDUsers_Username] ON [dbo].[YDUsers];
    PRINT 'IX_YDUsers_Username birakildi.';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_YDUsers_Username_Domain' AND object_id = OBJECT_ID('dbo.YDUsers'))
BEGIN
    CREATE UNIQUE INDEX [IX_YDUsers_Username_Domain]
        ON [dbo].[YDUsers] ([Username], [Domain]);
    PRINT 'IX_YDUsers_Username_Domain olusturuldu.';
END
ELSE
    PRINT 'IX_YDUsers_Username_Domain zaten var.';
GO

-- ------------------------------------------------------------- kontrol
SELECT TOP 20 Id, Username, [Domain], FullName, RepositoryName, IsActive
FROM dbo.YDUsers
ORDER BY Username;

-- ---------------------------------------------------- ZFGISTIRAK ORNEGI
-- Yeni domainden bir kullanici eklemek icin Domain degerini ACIKCA yazin.
-- Ayni kullanici adi diger domainde zaten varsa bile artik eklenebilir.
--
--   INSERT INTO dbo.YDUsers (Username, [Domain], FullName, Email, IsActive, CreatedAt, RepositoryName)
--   VALUES (N'ali', N'ZFGISTIRAK', N'Ali Veli', NULL, 1, SYSUTCDATETIME(), N'Cezayir');
--
-- Domain degeri appsettings.json'daki Ldap:Domains listesindeki Name ile
-- BIREBIR ayni olmali, yoksa giris yapan kullanici kaydini bulamaz.
