BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    ALTER TABLE [dbo].[Servers] ADD [ApplicationId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    ALTER TABLE [dbo].[Licenses] ADD [CompanyId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE TABLE [dbo].[Companies] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [CountryOfOrigin] nvarchar(150) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Companies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE TABLE [dbo].[Applications] (
        [Id] int NOT NULL IDENTITY,
        [CountryId] int NOT NULL,
        [Name] nvarchar(255) NOT NULL,
        [CompanyId] int NULL,
        [LicenseId] int NULL,
        [ApplicationType] int NOT NULL,
        [IsExternallyExposed] bit NOT NULL,
        [Url] nvarchar(500) NULL,
        [IsCloudApplication] bit NOT NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Applications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Applications_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Applications_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Applications_Licenses_LicenseId] FOREIGN KEY ([LicenseId]) REFERENCES [dbo].[Licenses] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE TABLE [dbo].[CompanyContacts] (
        [Id] int NOT NULL IDENTITY,
        [CompanyId] int NOT NULL,
        [PersonName] nvarchar(150) NULL,
        [Title] nvarchar(150) NULL,
        [Phone] nvarchar(50) NULL,
        [Email] nvarchar(150) NULL,
        CONSTRAINT [PK_CompanyContacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CompanyContacts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE INDEX [IX_Servers_ApplicationId] ON [dbo].[Servers] ([ApplicationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE INDEX [IX_Licenses_CompanyId] ON [dbo].[Licenses] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE INDEX [IX_Applications_CompanyId] ON [dbo].[Applications] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE INDEX [IX_Applications_CountryId] ON [dbo].[Applications] ([CountryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE INDEX [IX_Applications_LicenseId] ON [dbo].[Applications] ([LicenseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Companies_Name] ON [dbo].[Companies] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    CREATE INDEX [IX_CompanyContacts_CompanyId] ON [dbo].[CompanyContacts] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    ALTER TABLE [dbo].[Licenses] ADD CONSTRAINT [FK_Licenses_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    ALTER TABLE [dbo].[Servers] ADD CONSTRAINT [FK_Servers_Applications_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807125104_AddCompaniesAndApplications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807125104_AddCompaniesAndApplications', N'9.0.18');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807132310_AddCountryIdToCompanies'
)
BEGIN
    DROP INDEX [IX_Companies_Name] ON [dbo].[Companies];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807132310_AddCountryIdToCompanies'
)
BEGIN
    ALTER TABLE [dbo].[Companies] ADD [CountryId] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807132310_AddCountryIdToCompanies'
)
BEGIN

                    UPDATE dbo.Companies
                    SET CountryId = (SELECT TOP 1 Id FROM dbo.Countries ORDER BY Id)
                    WHERE CountryId = 0
                      AND EXISTS (SELECT 1 FROM dbo.Countries);
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807132310_AddCountryIdToCompanies'
)
BEGIN
    CREATE INDEX [IX_Companies_CountryId] ON [dbo].[Companies] ([CountryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807132310_AddCountryIdToCompanies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Companies_Name_CountryId] ON [dbo].[Companies] ([Name], [CountryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807132310_AddCountryIdToCompanies'
)
BEGIN
    ALTER TABLE [dbo].[Companies] ADD CONSTRAINT [FK_Companies_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807132310_AddCountryIdToCompanies'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807132310_AddCountryIdToCompanies', N'9.0.18');
END;

COMMIT;
GO

