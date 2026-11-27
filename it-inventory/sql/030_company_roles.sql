-- 030_company_roles.sql
--
-- Bir firmanin birden fazla rol ustlenmesini saglar.
--
-- NEDEN:
-- Companies.CompanyType tek bir deger tutuyor. Sahada ayni firma hem
-- tedarikci (Supplier) hem destek (Support) hem danismanlik (Consultant)
-- veriyor. Subenin ilk cozumu "her rol icin ayri firma kaydi acalim" idi;
-- bu mumkun degil, cunku (Name, CountryId) uzerinde unique index var ve
-- ikinci kayit reddediliyor. Rolu coklamanin dogru yeri bu tablo.
--
-- CompanyType KOLONU SILINMEDI:
-- Mevcut kayitlar, raporlar ve dis sorgular o kolona bakiyor. Uygulama
-- kaydederken rol kumesinin ilk degerini oraya da yaziyor, yani kolon her
-- zaman gecerli bir rol tutmaya devam ediyor. Ekranda gosterilen liste ise
-- bu tablodan geliyor.
--
-- GERI DONUS:
-- Tabloyu silmek yeterli (DROP TABLE dbo.CompanyRoles). Uygulama o durumda
-- CompanyType kolonuna duser ve coklu rol ozelligi oncesindeki gibi
-- davranir -- veri kaybi olmaz.
--
-- Idempotenttir, birden fazla kez calistirilabilir.

SET NOCOUNT ON;

IF OBJECT_ID('dbo.CompanyRoles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompanyRoles
    (
        Id        int IDENTITY(1,1) NOT NULL,
        CompanyId int NOT NULL,
        [Role]    int NOT NULL,
        CONSTRAINT PK_CompanyRoles PRIMARY KEY (Id),
        CONSTRAINT FK_CompanyRoles_Companies_CompanyId
            FOREIGN KEY (CompanyId) REFERENCES dbo.Companies (Id) ON DELETE CASCADE
    );
    PRINT 'dbo.CompanyRoles olusturuldu.';
END
ELSE
    PRINT 'dbo.CompanyRoles zaten var.';
GO

-- Ayni rol ayni firmada iki kez bulunmasin. Form coklu secim oldugu icin
-- cift gonderim mumkun; o durumda listede "Support, Support" gorunurdu.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_CompanyRoles_CompanyId_Role'
                 AND object_id = OBJECT_ID('dbo.CompanyRoles'))
BEGIN
    CREATE UNIQUE INDEX IX_CompanyRoles_CompanyId_Role
        ON dbo.CompanyRoles (CompanyId, [Role]);
    PRINT 'IX_CompanyRoles_CompanyId_Role olusturuldu.';
END
ELSE
    PRINT 'IX_CompanyRoles_CompanyId_Role zaten var.';
GO

-- Mevcut kayitlarin aktarimi: her firmanin bugunku CompanyType degeri
-- ilk rolu olarak yaziliyor. Rolu olmayan firma kalmiyor, dolayisiyla
-- ekranda bos rol listesi gorunmuyor.
--
-- NOT EXISTS kosulu betigi tekrar calistirmayi guvenli kiliyor: elle
-- eklenmis roller silinmiyor, ustune yazilmiyor.
INSERT INTO dbo.CompanyRoles (CompanyId, [Role])
SELECT c.Id, c.CompanyType
FROM dbo.Companies AS c
WHERE NOT EXISTS (SELECT 1 FROM dbo.CompanyRoles AS r
                  WHERE r.CompanyId = c.Id AND r.[Role] = c.CompanyType);

PRINT CONCAT('Aktarilan firma rolu: ', @@ROWCOUNT);
GO

-- Kontrol: hangi firma hangi role sahip. Her rol ayri satir.
-- (STRING_AGG kullanilmadi; eski SQL Server surumlerinde yok.)
SELECT c.Id,
       c.Name,
       c.CompanyType AS BirincilRolKodu,
       r.[Role]      AS RolKodu
FROM dbo.Companies AS c
LEFT JOIN dbo.CompanyRoles AS r ON r.CompanyId = c.Id
ORDER BY c.Name, r.[Role];
GO

-- Rol kodlari (ITInventory.Data.Common.CompanyType):
--   0 Vendor, 1 Supplier, 2 Support, 3 Manufacturer,
--   4 Reseller, 5 Consultant, 6 Partner, 7 Other
