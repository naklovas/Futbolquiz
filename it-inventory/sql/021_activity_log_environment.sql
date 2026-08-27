BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813140911_RenameActivityLogIpAddressToEnvironment'
)
BEGIN
    EXEC sp_rename N'[dbo].[ActivityLogs].[IpAddress]', N'EnvironmentName', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813140911_RenameActivityLogIpAddressToEnvironment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260813140911_RenameActivityLogIpAddressToEnvironment', N'9.0.18');
END;

COMMIT;
GO
