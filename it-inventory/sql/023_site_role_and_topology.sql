BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814110131_AddSiteRoleAndCountryTopology'
)
BEGIN
    ALTER TABLE [dbo].[Servers] ADD [SiteRole] nvarchar(20) NOT NULL DEFAULT N'Primary';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814110131_AddSiteRoleAndCountryTopology'
)
BEGIN
    ALTER TABLE [dbo].[PhysicalDevices] ADD [SiteRole] nvarchar(20) NOT NULL DEFAULT N'Primary';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814110131_AddSiteRoleAndCountryTopology'
)
BEGIN
    CREATE TABLE [dbo].[CountryTopologyFiles] (
        [CountryId] int NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(150) NOT NULL,
        [FileData] varbinary(max) NOT NULL,
        [FileSize] bigint NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        [UploadedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_CountryTopologyFiles] PRIMARY KEY ([CountryId]),
        CONSTRAINT [FK_CountryTopologyFiles_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814110131_AddSiteRoleAndCountryTopology'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814110131_AddSiteRoleAndCountryTopology', N'9.0.18');
END;

COMMIT;
GO
