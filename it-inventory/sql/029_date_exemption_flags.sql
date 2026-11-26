-- 029_date_exemption_flags.sql
--
-- Zorunlu tarih alanlarini karsiligi olmayan durumlar icin isaretlenebilir
-- hale getirir.
--
-- NEDEN:
-- Dort tarih alani da zorunluydu. Ama yeni cikmis bir cihazin yayimlanmis
-- kullanim omru sonu tarihi, suresiz bir lisansin bitis tarihi, ayri bakim
-- anlasmasi olmayan bir donanimin destek tarihleri YOKTUR. Kullanici ya
-- uydurma bir tarih giriyor ya da kaydi hic acmiyordu. Uydurma tarih bos
-- alandan kotudur: yanlis zamanda uyari uretir ve kimse sorgulamaz.
--
-- Alani sadece opsiyonel yapmak yetmezdi: bos bir tarih "henuz girilmedi"
-- ile "hic yok" arasini ayirt ettirmiyor. Bu bayraklar ayirt ettiriyor.
--
-- Idempotenttir.

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Licenses') AND name = 'IsPerpetual')
BEGIN
    ALTER TABLE dbo.Licenses
        ADD IsPerpetual bit NOT NULL CONSTRAINT DF_Licenses_IsPerpetual DEFAULT (0);
    PRINT 'Licenses.IsPerpetual eklendi.';
END
ELSE
    PRINT 'Licenses.IsPerpetual zaten var.';

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.PhysicalDevices') AND name = 'EndOfLifeNotAnnounced')
BEGIN
    ALTER TABLE dbo.PhysicalDevices
        ADD EndOfLifeNotAnnounced bit NOT NULL CONSTRAINT DF_PhysicalDevices_EolNotAnnounced DEFAULT (0);
    PRINT 'PhysicalDevices.EndOfLifeNotAnnounced eklendi.';
END
ELSE
    PRINT 'PhysicalDevices.EndOfLifeNotAnnounced zaten var.';

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.PhysicalDevices') AND name = 'NoSupportContract')
BEGIN
    ALTER TABLE dbo.PhysicalDevices
        ADD NoSupportContract bit NOT NULL CONSTRAINT DF_PhysicalDevices_NoSupportContract DEFAULT (0);
    PRINT 'PhysicalDevices.NoSupportContract eklendi.';
END
ELSE
    PRINT 'PhysicalDevices.NoSupportContract zaten var.';
GO

-- ------------------------------------------------------------- kontrol
-- Mevcut kayitlarin hepsi 0 (varsayilan) ile baslar: bugune kadar girilen
-- tarihler oldugu gibi kalir, kimsenin verisi degismez.
SELECT TOP 10 Id, LicenseName, ExpirationDate, IsPerpetual
FROM dbo.Licenses ORDER BY Id DESC;

SELECT TOP 10 Id, DeviceName, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate,
       EndOfLifeNotAnnounced, NoSupportContract
FROM dbo.PhysicalDevices ORDER BY Id DESC;
