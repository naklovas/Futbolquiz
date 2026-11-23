-- 028_yduser_default_domain.sql
--
-- Adinda "test" GECMEYEN kullanicilarin Domain degerini fintek.local yapar.
--
-- NEDEN:
-- 027 ile gelen Domain kolonu mevcut kayitlarda NULL. NULL olan kayitlar
-- varsayilan domaine ait sayiliyor, yani fintek.local kullanicilari zaten
-- giris yapabiliyor. Bu script o varsayimi ACIK HALE getiriyor: domain
-- listesinin sirasi degisirse (ZFGISTIRAK basa alinirsa) NULL kayitlar
-- sessizce yanlis domaine kayar, acikca yazilmis olanlar kaymaz.
--
-- Test kullanicilari (algeria_test, germany_test...) bilerek NULL
-- birakiliyor: AD'de karsiliklari yok, yalnizca DevLogin ile kullaniliyorlar.
--
-- Idempotenttir.

SET NOCOUNT ON;

-- ------------------------------------------------------------ onizleme
-- Once kimlerin degisecegini gorun. Degisiklik asagida.
SELECT Username, [Domain] AS MevcutDomain, RepositoryName, IsActive,
       CASE WHEN Username LIKE N'%test%' THEN N'ATLANACAK (adinda test var)'
            WHEN [Domain] = N'fintek.local' THEN N'zaten fintek.local'
            ELSE N'-> fintek.local' END AS Islem
FROM dbo.YDUsers
ORDER BY Islem, Username;

-- ------------------------------------------------------------- degisim
BEGIN TRANSACTION;

UPDATE dbo.YDUsers
SET [Domain] = N'fintek.local'
WHERE Username NOT LIKE N'%test%'
  AND ([Domain] IS NULL OR [Domain] <> N'fintek.local');

PRINT CONCAT(@@ROWCOUNT, ' kullanici fintek.local olarak isaretlendi.');

COMMIT TRANSACTION;

-- ------------------------------------------------------------- kontrol
SELECT ISNULL([Domain], N'(NULL -- varsayilan domaine ait sayilir)') AS DomainDegeri,
       COUNT(*) AS KullaniciSayisi
FROM dbo.YDUsers
GROUP BY [Domain]
ORDER BY KullaniciSayisi DESC;

SELECT Username, [Domain], RepositoryName, IsActive
FROM dbo.YDUsers
ORDER BY [Domain], Username;
