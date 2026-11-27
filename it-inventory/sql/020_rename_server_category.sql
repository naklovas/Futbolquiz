BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813125324_RenameServerCategory'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''ESXi / Physical Server''
    WHERE [Id] = 1;
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813125324_RenameServerCategory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260813125324_RenameServerCategory', N'9.0.18');
END;

COMMIT;
GO
