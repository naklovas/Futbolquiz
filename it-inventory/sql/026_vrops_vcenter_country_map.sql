-- 026_vrops_vcenter_country_map.sql
--
-- Aria'daki vCenter'lari dbo.Countries kayitlarina baglar.
--
-- NEDEN ADAY LISTESI:
-- vCenter adlari Ingilizce (ZBBULGARIA), Countries.Name ise Turkce olabilir
-- (Bulgaristan). Hangi yazimin kullanildigini bilmedigimiz icin her vCenter
-- icin birkac aday deniyoruz ve Name / DisplayName / Code sutunlarinin
-- ucune birden bakiyoruz. Ilk tutan aday kazaniyor.
--
-- ONCE @Uygula = 0 ile calistirin: hicbir sey yazmaz, neyin nereye
-- baglanacagini gosterir. Dogruysa @Uygula = 1 yapip tekrar calistirin.
--
-- Idempotenttir.

SET NOCOUNT ON;

DECLARE @Uygula bit = 0;        -- <<< once 0, dogruladiktan sonra 1

-- ------------------------------------------------------------- adaylar
DECLARE @Aday TABLE (Vcenter nvarchar(255), Sira int, Aday nvarchar(200));

INSERT INTO @Aday (Vcenter, Sira, Aday) VALUES
    (N'ZBALGERIA',    1, N'Cezayir'),      (N'ZBALGERIA',    2, N'Algeria'),       (N'ZBALGERIA',    3, N'DZ'),
    (N'ZBALBANIA',    1, N'Arnavutluk'),   (N'ZBALBANIA',    2, N'Albania'),       (N'ZBALBANIA',    3, N'AL'),
    (N'ZBAZERBAIJAN', 1, N'Azerbaycan'),   (N'ZBAZERBAIJAN', 2, N'Azerbaijan'),    (N'ZBAZERBAIJAN', 3, N'AZ'),
    (N'ZBBOSNIA',     1, N'Bosna Hersek'), (N'ZBBOSNIA',     2, N'Bosna-Hersek'),  (N'ZBBOSNIA',     3, N'Bosnia'),
    (N'ZBBOSNIA',     4, N'Bosnia and Herzegovina'),                               (N'ZBBOSNIA',     5, N'BA'),
    (N'ZBBULGARIA',   1, N'Bulgaristan'),  (N'ZBBULGARIA',   2, N'Bulgaria'),      (N'ZBBULGARIA',   3, N'BG'),
    (N'ZBGEORGIA',    1, N'Gurcistan'),    (N'ZBGEORGIA',    2, N'Gürcistan'),     (N'ZBGEORGIA',    3, N'Georgia'),
    (N'ZBGEORGIA',    4, N'GE'),
    (N'ZBGREECE',     1, N'Yunanistan'),   (N'ZBGREECE',     2, N'Greece'),        (N'ZBGREECE',     3, N'GR'),
    (N'ZBKOSOVA',     1, N'Kosova'),       (N'ZBKOSOVA',     2, N'Kosovo'),        (N'ZBKOSOVA',     3, N'XK'),
    (N'ZBMONTENEGRO', 1, N'Karadag'),      (N'ZBMONTENEGRO', 2, N'Karadağ'),       (N'ZBMONTENEGRO', 3, N'Montenegro'),
    (N'ZBMONTENEGRO', 4, N'ME'),
    (N'ZBRUSSIA',     1, N'Rusya'),        (N'ZBRUSSIA',     2, N'Russia'),        (N'ZBRUSSIA',     3, N'RU'),
    -- ZBLONDON bir sehir adi; ulke kaydi Ingiltere / Birlesik Krallik olabilir.
    (N'ZBLONDON',     1, N'Ingiltere'),    (N'ZBLONDON',     2, N'İngiltere'),     (N'ZBLONDON',     3, N'Birlesik Krallik'),
    (N'ZBLONDON',     4, N'Birleşik Krallık'),                                     (N'ZBLONDON',     5, N'United Kingdom'),
    (N'ZBLONDON',     6, N'Londra'),       (N'ZBLONDON',     7, N'London'),        (N'ZBLONDON',     8, N'GB'),
    -- KZIBANK = Kazakhstan Ziraat International Bank. BU BIR TAHMIN;
    -- eslesmezse asagidaki uyari listesinde gorunur.
    (N'KZIBANK',      1, N'Kazakistan'),   (N'KZIBANK',      2, N'Kazakhstan'),    (N'KZIBANK',      3, N'KZ');

-- ------------------------------------------------------------ eslestir
DECLARE @Eslesen TABLE (Vcenter nvarchar(255), CountryId int, UlkeAdi nvarchar(200), TutanAday nvarchar(200));

INSERT INTO @Eslesen (Vcenter, CountryId, UlkeAdi, TutanAday)
SELECT x.Vcenter, x.Id, x.Name, x.Aday
FROM (
    SELECT a.Vcenter, a.Aday, c.Id, c.Name,
           ROW_NUMBER() OVER (PARTITION BY a.Vcenter ORDER BY a.Sira) AS sira
    FROM @Aday a
    JOIN dbo.Countries c
      ON  c.Name        = a.Aday
       OR c.DisplayName = a.Aday
       OR c.Code        = a.Aday
       OR c.DisplayName = N'ZiraatBank ' + a.Aday
) x
WHERE x.sira = 1;

-- --------------------------------------------------------------- rapor
SELECT N'ESLESTI' AS Durum, e.Vcenter, e.UlkeAdi, e.TutanAday
FROM @Eslesen e
UNION ALL
SELECT N'ESLESMEDI', a.Vcenter, NULL, NULL
FROM (SELECT DISTINCT Vcenter FROM @Aday) a
WHERE NOT EXISTS (SELECT 1 FROM @Eslesen e WHERE e.Vcenter = a.Vcenter)
ORDER BY Durum, Vcenter;

IF @Uygula = 0
BEGIN
    PRINT '';
    PRINT '@Uygula = 0 -- hicbir sey YAZILMADI.';
    PRINT 'Yukaridaki eslesme dogruysa @Uygula = 1 yapip tekrar calistirin.';
    PRINT 'ESLESMEDI satirlari icin ilgili ulkenin Countries.Name degerini';
    PRINT '@Aday listesine yeni bir satir olarak ekleyin.';
    RETURN;
END

-- ------------------------------------------------------------- uygula
BEGIN TRANSACTION;

UPDATE d
SET d.CountryId = e.CountryId, d.ResourceKind = N'VMwareAdapter Instance', d.IsActive = 1
FROM dbo.VropsDatacenters d
JOIN @Eslesen e ON e.Vcenter = d.DatacenterName;

INSERT INTO dbo.VropsDatacenters (DatacenterName, ResourceKind, CountryId, SiteRole, IsActive)
SELECT e.Vcenter, N'VMwareAdapter Instance', e.CountryId, 0 /* Primary */, 1
FROM @Eslesen e
WHERE NOT EXISTS (SELECT 1 FROM dbo.VropsDatacenters d WHERE d.DatacenterName = e.Vcenter);

COMMIT TRANSACTION;

-- --------------------------------------------------------------- sonuc
SELECT d.DatacenterName AS Vcenter, d.ResourceKind, c.Name AS Ulke, d.IsActive
FROM dbo.VropsDatacenters d
JOIN dbo.Countries c ON c.Id = d.CountryId
ORDER BY d.IsActive DESC, c.Name;
