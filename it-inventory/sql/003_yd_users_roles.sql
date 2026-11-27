-- dbo.YDRoles / dbo.YDUsers / dbo.YDUserRoles tabloları production ortamınızda
-- zaten var olduğu varsayılmıştı (bkz. README). Yeni/boş bir SQL Server'da
-- çalışıyorsanız bu tablolar mevcut değildir; bu script onları oluşturur,
-- 3 rolü seed eder ve "visikhan" kullanıcısını admin olarak tanımlar.
-- Idempotenttir, birden fazla çalıştırılabilir.

IF OBJECT_ID(N'[dbo].[YDRoles]') IS NULL
BEGIN
    CREATE TABLE [dbo].[YDRoles] (
        [Id] int NOT NULL IDENTITY,
        [RoleName] nvarchar(50) NOT NULL,
        [Description] nvarchar(255) NULL,
        CONSTRAINT [PK_YDRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF OBJECT_ID(N'[dbo].[YDUsers]') IS NULL
BEGIN
    CREATE TABLE [dbo].[YDUsers] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(100) NOT NULL,
        [FullName] nvarchar(100) NOT NULL,
        [Email] nvarchar(100) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        -- Kullanıcının bağlı olduğu ülke/şube (Ziraat_YD.RepositoryName ile aynı değer kümesi). Admin için NULL bırakılabilir.
        [RepositoryName] nvarchar(255) NULL,
        CONSTRAINT [PK_YDUsers] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_YDUsers_Username] ON [dbo].[YDUsers] ([Username]);
END;
GO

IF OBJECT_ID(N'[dbo].[YDUserRoles]') IS NULL
BEGIN
    CREATE TABLE [dbo].[YDUserRoles] (
        [UserId] int NOT NULL,
        [RoleId] int NOT NULL,
        CONSTRAINT [PK_YDUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_YDUserRoles_YDUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[YDUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_YDUserRoles_YDRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[YDRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.YDRoles WHERE RoleName = N'admin')
    INSERT INTO dbo.YDRoles (RoleName, Description)
    VALUES (N'admin', N'Tüm ülkeler için tam yetki (kullanıcı/rol/ülke/cihaz profili yönetimi dahil)');

IF NOT EXISTS (SELECT 1 FROM dbo.YDRoles WHERE RoleName = N'country_manager')
    INSERT INTO dbo.YDRoles (RoleName, Description)
    VALUES (N'country_manager', N'Sadece kendi ülkesi/şubesi için ekleme, düzenleme ve görüntüleme yetkisi');

IF NOT EXISTS (SELECT 1 FROM dbo.YDRoles WHERE RoleName = N'country_view_only')
    INSERT INTO dbo.YDRoles (RoleName, Description)
    VALUES (N'country_view_only', N'Sadece kendi ülkesi/şubesi için görüntüleme yetkisi (salt okunur)');
GO

-- "visikhan" kullanıcısını admin yapar. Username, AD'deki (fintek.local) sAMAccountName
-- ile birebir aynı olmalı (giriş ekranında @ yazmadan girilen kısım). FullName/Email
-- alanlarını isterseniz kendinize göre değiştirin.
IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'visikhan')
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'visikhan', N'Visikhan', NULL, 1, SYSUTCDATETIME(), NULL);

INSERT INTO dbo.YDUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.YDUsers u
CROSS JOIN dbo.YDRoles r
WHERE u.Username = N'visikhan' AND r.RoleName = N'admin'
AND NOT EXISTS (
    SELECT 1 FROM dbo.YDUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id
);
GO
