BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813083451_AddActivityLogTable'
)
BEGIN
    CREATE TABLE [dbo].[ActivityLogs] (
        [Id] int NOT NULL IDENTITY,
        [CreatedAt] datetime2 NOT NULL,
        [Username] nvarchar(100) NOT NULL,
        [FullName] nvarchar(150) NULL,
        [CountryName] nvarchar(150) NULL,
        [Action] nvarchar(50) NOT NULL,
        [EntityType] nvarchar(100) NOT NULL,
        [EntityName] nvarchar(255) NULL,
        [Details] nvarchar(1000) NULL,
        [IpAddress] nvarchar(64) NULL,
        CONSTRAINT [PK_ActivityLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813083451_AddActivityLogTable'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_CreatedAt] ON [dbo].[ActivityLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813083451_AddActivityLogTable'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_EntityType] ON [dbo].[ActivityLogs] ([EntityType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813083451_AddActivityLogTable'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_Username] ON [dbo].[ActivityLogs] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813083451_AddActivityLogTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260813083451_AddActivityLogTable', N'9.0.18');
END;

COMMIT;
GO
