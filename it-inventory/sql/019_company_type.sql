BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813110445_AddCompanyType'
)
BEGIN
    ALTER TABLE [dbo].[Companies] ADD [CompanyType] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813110445_AddCompanyType'
)
BEGIN
    ALTER TABLE [dbo].[Companies] ADD [OtherTypeDescription] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813110445_AddCompanyType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260813110445_AddCompanyType', N'9.0.18');
END;

COMMIT;
GO
