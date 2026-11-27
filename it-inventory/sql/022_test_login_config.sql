BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814055103_AddTestLoginConfig'
)
BEGIN
    CREATE TABLE [dbo].[TestLoginConfig] (
        [Id] int NOT NULL,
        [PasswordHash] nvarchar(200) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_TestLoginConfig] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814055103_AddTestLoginConfig'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814055103_AddTestLoginConfig', N'9.0.18');
END;

COMMIT;
GO

-- Seed row Id=1 with the PBKDF2 hash of the CURRENT test password ("12345") so behavior is
-- unchanged for existing testers -- only where it's stored changes (DB instead of a literal
-- in appsettings.json/source). Format: "{iterations}.{saltBase64}.{hashBase64}" (see
-- ITInventory.Web/Services/TestLoginPasswordHasher.cs). Only inserted if the row is missing,
-- so re-running this script never overwrites a password you've since changed.
IF NOT EXISTS (SELECT 1 FROM [dbo].[TestLoginConfig] WHERE [Id] = 1)
BEGIN
    INSERT INTO [dbo].[TestLoginConfig] ([Id], [PasswordHash], [UpdatedAt], [UpdatedBy])
    VALUES (
        1,
        N'100000.pDubmjvtW/Mtfo63JOamQQ==.I50YOtorB5PYHszHlNSADmYOL9uZlLHNr50v0T+O4X4=',
        SYSUTCDATETIME(),
        N'seed-script'
    );
END;
GO
