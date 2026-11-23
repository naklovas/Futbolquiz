-- 024_vrops_datacenters_and_algeria.sql
--
-- 1) dbo.VropsDatacenters tablosu  (vROps datacenter adi -> ulke eslemesi)
-- 2) Cezayir ulkesi
-- 3) ZALG_DC eslemesi
-- 4) Cezayir icin test kullanicisi
--
-- Idempotenttir; birden fazla kez calistirilabilir.
--
-- >>> TEST KULLANICISI HAKKINDA ONEMLI NOT <<<
-- "Log In as Test User" ozelligi KALDIRILDI (Weak Cryptographic Hash bulgusu
-- yuzunden; PBKDF2 salt'i kayitli metinden okunuyordu). Artik giris YALNIZCA
-- AD/LDAP ile yapiliyor.
--
-- Yani asagidaki satir tek basina giris saglamaz: @TestUsername degeri
-- fintek.local'de GERCEKTEN VAR OLAN bir hesabin kullanici adi olmalidir.
-- Kendi hesabinizla Cezayir'i test etmek isterseniz en pratigi, kendi
-- kullanici adinizi buraya yazmaktir (ama o zaman kendi ulkeniz degisir --
-- testten sonra geri almayi unutmayin; en altta geri alma sorgusu var).

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- ---------------------------------------------------------------- 1) tablo
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VropsDatacenters' AND SCHEMA_NAME(schema_id) = 'dbo')
BEGIN
    CREATE TABLE [dbo].[VropsDatacenters] (
        [Id]             int NOT NULL IDENTITY,
        [DatacenterName] nvarchar(255) NOT NULL,
        [CountryId]      int NOT NULL,
        [SiteRole]       int NOT NULL CONSTRAINT [DF_VropsDatacenters_SiteRole] DEFAULT (0),
        [IsActive]       bit NOT NULL CONSTRAINT [DF_VropsDatacenters_IsActive] DEFAULT (1),
        CONSTRAINT [PK_VropsDatacenters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VropsDatacenters_Countries_CountryId]
            FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE CASCADE
    );

    -- Ayni datacenter iki ulkeye baglanamaz.
    CREATE UNIQUE INDEX [IX_VropsDatacenters_DatacenterName]
        ON [dbo].[VropsDatacenters] ([DatacenterName]);

    PRINT 'dbo.VropsDatacenters olusturuldu.';
END
ELSE
    PRINT 'dbo.VropsDatacenters zaten var, atlandi.';

-- ------------------------------------------------------------- 2) Cezayir
-- Name alani Ziraat_YD.RepositoryName ile eslesmek zorunda (Device Pool bunu
-- kullaniyor). Nessus tarafinda Cezayir'in repository adi FARKLIYSA asagidaki
-- degeri ona gore duzeltin; yoksa Device Pool bu ulke icin bos gelir.
DECLARE @CezayirName nvarchar(200) = N'Cezayir';

IF NOT EXISTS (SELECT 1 FROM dbo.Countries WHERE Name = @CezayirName)
BEGIN
    INSERT INTO dbo.Countries (Name, DisplayName, Code, IsActive, CreatedAt)
    VALUES (@CezayirName, N'ZiraatBank Algeria', N'DZ', 1, SYSUTCDATETIME());
    PRINT 'Cezayir ulkesi eklendi.';
END
ELSE
    PRINT 'Cezayir ulkesi zaten var.';

DECLARE @CezayirId int = (SELECT Id FROM dbo.Countries WHERE Name = @CezayirName);

-- --------------------------------------------------------- 3) ZALG_DC esleme
IF NOT EXISTS (SELECT 1 FROM dbo.VropsDatacenters WHERE DatacenterName = N'ZALG_DC')
BEGIN
    INSERT INTO dbo.VropsDatacenters (DatacenterName, CountryId, SiteRole, IsActive)
    VALUES (N'ZALG_DC', @CezayirId, 0 /* Primary */, 1);
    PRINT 'ZALG_DC -> Cezayir eslemesi eklendi.';
END
ELSE
BEGIN
    UPDATE dbo.VropsDatacenters SET CountryId = @CezayirId, IsActive = 1
    WHERE DatacenterName = N'ZALG_DC' AND CountryId <> @CezayirId;
    PRINT 'ZALG_DC eslemesi zaten var.';
END

-- ------------------------------------------------------- 4) test kullanicisi
-- algeria_test, germany_test / bulgaria_test ile ayni desende olusturulur.
-- DIKKAT: Bu satir TEK BASINA GIRIS SAGLAMAZ. "Log In as Test User" ozelligi
-- kaldirildi (Weak Cryptographic Hash bulgusu); giris artik yalnizca AD/LDAP ile.
-- algeria_test fintek.local'de yoksa bu kayit sadece veritabaninda durur.
IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'algeria_test')
BEGIN
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'algeria_test', N'Algeria Test User', NULL, 1, SYSUTCDATETIME(), @CezayirName);
    PRINT 'algeria_test eklendi.';
END
ELSE
BEGIN
    UPDATE dbo.YDUsers SET RepositoryName = @CezayirName, IsActive = 1
    WHERE Username = N'algeria_test' AND (RepositoryName <> @CezayirName OR IsActive = 0);
    PRINT 'algeria_test zaten var, Cezayir''a baglandi.';
END

-- country_manager: kendi ulkesinde goruntuler, ekler, duzenler.
INSERT INTO dbo.YDUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.YDUsers u CROSS JOIN dbo.YDRoles r
WHERE u.Username = N'algeria_test'
  AND r.RoleName = N'country_manager'
  AND NOT EXISTS (SELECT 1 FROM dbo.YDUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

-- ------------------------------------------- 5) GERCEKTEN giris yapacak hesap
-- Cezayir'i ekranda test etmek icin AD'de VAR OLAN bir hesap gerekiyor.
-- En pratigi kendi kullanici adiniz. Bos birakirsaniz bu bolum atlanir ve
-- sonuc izgarasinda bunu acikca gorursunuz.
DECLARE @AdUsername nvarchar(100) = N'';   -- <<< kendi AD kullanici adiniz

IF @AdUsername <> N''
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = @AdUsername)
        INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
        VALUES (@AdUsername, @AdUsername, NULL, 1, SYSUTCDATETIME(), @CezayirName);
    ELSE
        UPDATE dbo.YDUsers SET RepositoryName = @CezayirName, IsActive = 1
        WHERE Username = @AdUsername;

    INSERT INTO dbo.YDUserRoles (UserId, RoleId)
    SELECT u.Id, r.Id
    FROM dbo.YDUsers u CROSS JOIN dbo.YDRoles r
    WHERE u.Username = @AdUsername
      AND r.RoleName = N'country_manager'
      AND NOT EXISTS (SELECT 1 FROM dbo.YDUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

    PRINT 'AD hesabi Cezayir''a baglandi: ' + @AdUsername;
END

COMMIT TRANSACTION;

-- ---------------------------------------------------------------- kontrol
SELECT c.Id, c.Name, c.DisplayName, c.Code, c.IsActive
FROM dbo.Countries c WHERE c.Name = N'Cezayir';

SELECT d.Id, d.DatacenterName, c.Name AS Ulke, d.SiteRole, d.IsActive
FROM dbo.VropsDatacenters d
JOIN dbo.Countries c ON c.Id = d.CountryId;

SELECT u.Username, u.FullName, u.RepositoryName, u.IsActive,
       STUFF((SELECT ', ' + r.RoleName FROM dbo.YDUserRoles ur
              JOIN dbo.YDRoles r ON r.Id = ur.RoleId
              WHERE ur.UserId = u.Id FOR XML PATH('')), 1, 2, '') AS Roller,
       CASE WHEN u.Username = N'algeria_test'
            THEN N'AD hesabi degil -- GIRIS YAPAMAZ, sadece kayit'
            ELSE N'AD hesabi ise giris yapabilir' END AS GirisDurumu
FROM dbo.YDUsers u WHERE u.RepositoryName = N'Cezayir';

-- ------------------------------------------------------------- GERI ALMA
-- 1) algeria_test'i silmek icin (AD'de karsiligi yok, test sonrasi temizlik):
--
--   DELETE ur FROM dbo.YDUserRoles ur
--   JOIN dbo.YDUsers u ON u.Id = ur.UserId
--   WHERE u.Username = N'algeria_test';
--   DELETE FROM dbo.YDUsers WHERE Username = N'algeria_test';
--
-- 2) Kendi hesabinizi Cezayir'a bagladiysaniz, test bitince eski ulkenize dondurun:
--
--   UPDATE dbo.YDUsers SET RepositoryName = N'ESKI_ULKENIZ'
--   WHERE Username = N'SIZIN_KULLANICI_ADINIZ';
