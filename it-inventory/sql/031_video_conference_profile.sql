-- 031_video_conference_profile.sql
--
-- Cihaz profili katalogua video konferans sistemlerini ekler.
--
-- NEDEN:
-- Ses/Görüntü (Audio/Video) kategorisinin altinda IP telefon ve medya
-- oynatici profilleri vardi, video konferans icin karsiligi yoktu.
--
-- NE ISE YARAR, NE ISE YARAMAZ:
-- Fiziksel cihaz formunda profil SECILMIYOR -- orada yalnizca kategori
-- seciliyor ve Audio/Video zaten mevcut. Yani bir VC codec'ini ELLE
-- eklemek icin bu betige ihtiyaciniz yok, dun de ekleyebiliyordunuz.
-- Profil, Cihaz Havuzu'ndan (Nessus/Tenable) gelen kayitlarin hangi
-- kategoriye dusecegini belirliyor. Bu satir, tarama servisi
-- "Video Konferans Sistemi" profilini raporlamaya basladiginda o
-- cihazlarin Ses/Görüntü kategorisine dusmesini sagliyor; servis baska
-- bir metin yaziyorsa katalogdaki ProfileName degerini ona gore
-- duzeltmeniz gerekir (Yonetim > Cihaz Profilleri ekranindan kategori
-- eslemesi degistirilebilir, profil adi degistirilemez).
--
-- Idempotenttir.

SET NOCOUNT ON;

DECLARE @SesGoruntuId int = (
    SELECT TOP 1 Id FROM dbo.DeviceCategories
    WHERE Name IN (N'Audio/Video', N'Ses/Görüntü')
    ORDER BY Id);

IF @SesGoruntuId IS NULL
BEGIN
    RAISERROR(N'Audio/Video kategorisi bulunamadi; once 005_device_profile_displayname_and_category_names.sql calistirilmali.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.DeviceProfileCatalog WHERE ProfileName = N'Video Konferans Sistemi')
BEGIN
    PRINT 'Video Konferans Sistemi profili zaten var.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.DeviceProfileCatalog WHERE Id = 48)
BEGIN
    -- Id'yi acikca 48 veriyoruz: uygulamanin kendi seed tanimi da bu satiri
    -- 48 olarak uretiyor, boylece sifirdan kurulan bir veritabani ile
    -- mevcut veritabani ayni Id'yi tasiyor.
    SET IDENTITY_INSERT dbo.DeviceProfileCatalog ON;
    INSERT INTO dbo.DeviceProfileCatalog (Id, ProfileName, DisplayName, CategoryId)
    VALUES (48, N'Video Konferans Sistemi', N'Video Conference System', @SesGoruntuId);
    SET IDENTITY_INSERT dbo.DeviceProfileCatalog OFF;
    PRINT 'Video Konferans Sistemi profili eklendi (Id = 48).';
END
ELSE
BEGIN
    -- 48 doluysa Id'yi veritabani versin; eslesme ProfileName uzerinden
    -- yapildigi icin Id'nin farkli olmasi uygulamayi etkilemez.
    INSERT INTO dbo.DeviceProfileCatalog (ProfileName, DisplayName, CategoryId)
    VALUES (N'Video Konferans Sistemi', N'Video Conference System', @SesGoruntuId);
    PRINT CONCAT('Video Konferans Sistemi profili eklendi (Id = ', SCOPE_IDENTITY(), ').');
END
GO

-- Kontrol: Ses/Görüntü kategorisinin altindaki profiller.
SELECT p.Id, p.ProfileName, p.DisplayName, k.Name AS Kategori
FROM dbo.DeviceProfileCatalog AS p
INNER JOIN dbo.DeviceCategories AS k ON k.Id = p.CategoryId
WHERE k.Name IN (N'Audio/Video', N'Ses/Görüntü')
ORDER BY p.Id;
GO
