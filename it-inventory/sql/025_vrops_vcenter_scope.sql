-- 025_vrops_vcenter_scope.sql
--
-- Ulke ayrimini DATACENTER adindan VCENTER adina tasir.
--
-- NEDEN:
-- Datacenter adlari tutarsiz (ZALG_DC, VxRail-Datacenter, Podgorica...) ve
-- ayni ad birden fazla ulkede gecebiliyor -- "VxRail-Datacenter" hem
-- Azerbaycan'da hem baska yerde var. vCenter adlari ise temiz ve ulke
-- basina tek: ZBALGERIA, ZBBULGARIA, ZBMONTENEGRO...
--
-- Tabloyu YENIDEN ADLANDIRMIYORUZ. Bunun yerine ResourceKind kolonu
-- ekliyoruz; ayni tablo hem datacenter hem vCenter eslemesi tutabiliyor.
-- Ayrim bir kez degisti, tekrar degisebilir; satirin neyi isaret ettigini
-- kolonda tutmak, her degisiklikte tablo adi degistirmekten iyi.
-- (Tablo adinin artik yaniltici oldugunu biliyorum -- not dusuldu.)
--
-- Idempotenttir.

SET NOCOUNT ON;

-- --------------------------------------------------------- 1) yeni kolon
-- Kolonu islem DISINDA ekliyoruz: ayni toplu iste hem ekleyip hem
-- kullanamayiz, GO gerekiyor, ve bir islemi GO'nun iki yanina yaymak
-- kirilgan. 005/006/007 scriptleri de boyle yapiyor.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.VropsDatacenters') AND name = 'ResourceKind')
BEGIN
    ALTER TABLE dbo.VropsDatacenters
        ADD ResourceKind nvarchar(100) NOT NULL
            CONSTRAINT DF_VropsDatacenters_ResourceKind DEFAULT (N'Datacenter');
    PRINT 'ResourceKind kolonu eklendi.';
END
ELSE
    PRINT 'ResourceKind kolonu zaten var.';
GO

-- --------------------------------------------------------- 2) ZBALGERIA
BEGIN TRANSACTION;

DECLARE @CezayirName nvarchar(200) = N'Cezayir';
DECLARE @CezayirId int = (SELECT Id FROM dbo.Countries WHERE Name = @CezayirName);

IF @CezayirId IS NULL
BEGIN
    RAISERROR(N'Cezayir ulkesi yok. Once 024 scriptini calistirin.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.VropsDatacenters WHERE DatacenterName = N'ZBALGERIA')
BEGIN
    INSERT INTO dbo.VropsDatacenters (DatacenterName, ResourceKind, CountryId, SiteRole, IsActive)
    VALUES (N'ZBALGERIA', N'VMwareAdapter Instance', @CezayirId, 0 /* Primary */, 1);
    PRINT 'ZBALGERIA -> Cezayir eslemesi eklendi.';
END
ELSE
BEGIN
    UPDATE dbo.VropsDatacenters
    SET ResourceKind = N'VMwareAdapter Instance', CountryId = @CezayirId, IsActive = 1
    WHERE DatacenterName = N'ZBALGERIA';
    PRINT 'ZBALGERIA eslemesi guncellendi.';
END

-- ------------------------------------------------- 3) eski satiri kapat
-- ZALG_DC satirini SILMIYORUZ, pasife aliyoruz: vCenter yaklasimi
-- beklendigi gibi calismazsa tek bir UPDATE ile geri donulebilsin.
UPDATE dbo.VropsDatacenters SET IsActive = 0
WHERE DatacenterName = N'ZALG_DC' AND IsActive = 1;

IF @@ROWCOUNT > 0
    PRINT 'ZALG_DC (datacenter) eslemesi pasife alindi.';

COMMIT TRANSACTION;

-- ------------------------------------------------------------- kontrol
SELECT d.Id, d.DatacenterName AS KaynakAdi, d.ResourceKind, c.Name AS Ulke,
       d.SiteRole, d.IsActive
FROM dbo.VropsDatacenters d
JOIN dbo.Countries c ON c.Id = d.CountryId
ORDER BY c.Name, d.IsActive DESC, d.DatacenterName;

-- --------------------------------------------------------- DIGER ULKELER
-- vCenter adlari Aria Fields ekranindaki vCenters tablosundan alindi.
-- Countries tablosunda karsiligi olanlari asagidaki gibi ekleyin;
-- Name degeri Countries.Name ile BIREBIR eslesmeli.
--
--   INSERT INTO dbo.VropsDatacenters (DatacenterName, ResourceKind, CountryId, SiteRole, IsActive)
--   SELECT N'ZBBULGARIA', N'VMwareAdapter Instance', c.Id, 0, 1
--   FROM dbo.Countries c WHERE c.Name = N'Bulgaristan'
--     AND NOT EXISTS (SELECT 1 FROM dbo.VropsDatacenters WHERE DatacenterName = N'ZBBULGARIA');
--
-- Aria'daki vCenter'lar: ZBALBANIA, ZBALGERIA, ZBAZERBAIJAN, ZBBOSNIA,
-- ZBBULGARIA, ZBGEORGIA, ZBGREECE, ZBKOSOVA, ZBLONDON, ZBMONTENEGRO,
-- ZBRUSSIA, KZIBANK
