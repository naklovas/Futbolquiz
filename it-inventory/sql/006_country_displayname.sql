BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806083155_AddCountryDisplayName'
)
BEGIN
    ALTER TABLE [dbo].[Countries] ADD [DisplayName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806083155_AddCountryDisplayName'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260806083155_AddCountryDisplayName', N'8.0.11');
END;
GO

COMMIT;
GO

-- Known institution/display names. Fill in the rest via Admin > Countries as they become known.
UPDATE dbo.Countries SET DisplayName = N'ZiraatBank AG' WHERE Name = N'Almanya';
GO

