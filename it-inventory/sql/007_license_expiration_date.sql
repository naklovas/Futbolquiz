BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806092015_AddLicenseExpirationDate'
)
BEGIN
    ALTER TABLE [dbo].[Licenses] ADD [ExpirationDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806092015_AddLicenseExpirationDate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260806092015_AddLicenseExpirationDate', N'8.0.11');
END;
GO

COMMIT;
GO

