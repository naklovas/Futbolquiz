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

                    EXEC(N'
                        UPDATE dbo.Companies
                        SET CountryId = (SELECT TOP 1 Id FROM dbo.Countries ORDER BY Id)
                        WHERE CountryId = 0
                          AND EXISTS (SELECT 1 FROM dbo.Countries);
                    ');
                
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

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    CREATE TABLE [dbo].[OriginCountries] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_OriginCountries] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OriginCountries_Name] ON [dbo].[OriginCountries] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Name', N'IsActive', N'CreatedAt') AND [object_id] = OBJECT_ID(N'[dbo].[OriginCountries]'))
        SET IDENTITY_INSERT [dbo].[OriginCountries] ON;
    EXEC(N'INSERT INTO [dbo].[OriginCountries] ([Name], [IsActive], [CreatedAt])
    VALUES (N''Afghanistan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Albania'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Algeria'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Andorra'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Angola'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Argentina'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Armenia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Australia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Austria'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Azerbaijan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Bahamas'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Bahrain'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Bangladesh'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Barbados'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Belarus'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Belgium'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Belize'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Benin'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Bhutan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Bolivia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Bosnia and Herzegovina'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Botswana'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Brazil'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Brunei'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Bulgaria'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Burkina Faso'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Burundi'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Cabo Verde'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Cambodia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Cameroon'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Canada'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Central African Republic'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Chad'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Chile'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''China'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Colombia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Comoros'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Congo'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Costa Rica'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Croatia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Cuba'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Cyprus'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z'');
    INSERT INTO [dbo].[OriginCountries] ([Name], [IsActive], [CreatedAt])
    VALUES (N''Czechia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Democratic Republic of the Congo'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Denmark'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Djibouti'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Dominica'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Dominican Republic'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Ecuador'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Egypt'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''El Salvador'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Equatorial Guinea'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Eritrea'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Estonia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Eswatini'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Ethiopia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Fiji'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Finland'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''France'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Gabon'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Gambia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Georgia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Germany'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Ghana'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Greece'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Grenada'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Guatemala'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Guinea'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Guinea-Bissau'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Guyana'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Haiti'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Honduras'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Hungary'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Iceland'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''India'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Indonesia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Iran'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Iraq'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Ireland'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Israel'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Italy'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Ivory Coast'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Jamaica'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Japan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z'');
    INSERT INTO [dbo].[OriginCountries] ([Name], [IsActive], [CreatedAt])
    VALUES (N''Jordan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Kazakhstan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Kenya'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Kiribati'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Kosovo'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Kuwait'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Kyrgyzstan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Laos'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Latvia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Lebanon'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Lesotho'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Liberia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Libya'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Liechtenstein'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Lithuania'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Luxembourg'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Madagascar'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Malawi'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Malaysia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Maldives'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Mali'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Malta'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Marshall Islands'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Mauritania'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Mauritius'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Mexico'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Micronesia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Moldova'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Monaco'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Mongolia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Montenegro'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Morocco'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Mozambique'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Myanmar'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Namibia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Nauru'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Nepal'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Netherlands'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''New Zealand'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Nicaragua'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Niger'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Nigeria'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z'');
    INSERT INTO [dbo].[OriginCountries] ([Name], [IsActive], [CreatedAt])
    VALUES (N''North Korea'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''North Macedonia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Norway'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Oman'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Pakistan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Palau'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Palestine'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Panama'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Papua New Guinea'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Paraguay'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Peru'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Philippines'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Poland'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Portugal'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Qatar'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Romania'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Russia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Rwanda'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Saint Kitts and Nevis'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Saint Lucia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Saint Vincent and the Grenadines'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Samoa'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''San Marino'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Sao Tome and Principe'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Saudi Arabia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Senegal'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Serbia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Seychelles'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Sierra Leone'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Singapore'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Slovakia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Slovenia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Solomon Islands'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Somalia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''South Africa'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''South Korea'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''South Sudan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Spain'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Sri Lanka'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Sudan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Suriname'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Sweden'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z'');
    INSERT INTO [dbo].[OriginCountries] ([Name], [IsActive], [CreatedAt])
    VALUES (N''Switzerland'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Syria'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Taiwan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Tajikistan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Tanzania'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Thailand'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Timor-Leste'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Togo'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Tonga'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Trinidad and Tobago'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Tunisia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Turkey'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Turkmenistan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Tuvalu'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Uganda'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Ukraine'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''United Arab Emirates'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''United Kingdom'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''United States'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Uruguay'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Uzbekistan'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Vanuatu'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Vatican City'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Venezuela'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Vietnam'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Yemen'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Zambia'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z''),
    (N''Zimbabwe'', CAST(1 AS bit), ''2026-08-07T00:00:00.0000000Z'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Name', N'IsActive', N'CreatedAt') AND [object_id] = OBJECT_ID(N'[dbo].[OriginCountries]'))
        SET IDENTITY_INSERT [dbo].[OriginCountries] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    ALTER TABLE [dbo].[Companies] ADD [OriginCountryId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN

                    EXEC(N'
                        UPDATE c
                        SET c.OriginCountryId = oc.Id
                        FROM dbo.Companies c
                        INNER JOIN dbo.OriginCountries oc ON oc.Name = c.CountryOfOrigin
                        WHERE c.CountryOfOrigin IS NOT NULL;
                    ');
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    DECLARE @var sysname;
    SELECT @var = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[dbo].[Companies]') AND [c].[name] = N'CountryOfOrigin');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Companies] DROP CONSTRAINT [' + @var + '];');
    ALTER TABLE [dbo].[Companies] DROP COLUMN [CountryOfOrigin];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    CREATE INDEX [IX_Companies_OriginCountryId] ON [dbo].[Companies] ([OriginCountryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    ALTER TABLE [dbo].[Companies] ADD CONSTRAINT [FK_Companies_OriginCountries_OriginCountryId] FOREIGN KEY ([OriginCountryId]) REFERENCES [dbo].[OriginCountries] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807135346_AddOriginCountriesTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807135346_AddOriginCountriesTable', N'9.0.18');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807141454_AddLocationsTable'
)
BEGIN
    CREATE TABLE [dbo].[Locations] (
        [Id] int NOT NULL IDENTITY,
        [CountryId] int NOT NULL,
        [Branch] nvarchar(200) NOT NULL,
        [Class] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Locations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Locations_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807141454_AddLocationsTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Locations_CountryId_Branch] ON [dbo].[Locations] ([CountryId], [Branch]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807141454_AddLocationsTable'
)
BEGIN

                    INSERT INTO dbo.Locations (CountryId, Branch, Class, IsActive, CreatedAt)
                    SELECT c.Id, v.Branch, v.Class, 1, '2026-08-07T00:00:00'
                    FROM (VALUES
        (N'İNGİLTERE', N'LONDRA', N'Yurtdışı Şube'),
        (N'KOSOVA', N'PRİŞTİNE', N'Yurtdışı Şube'),
        (N'KOSOVA', N'PRİZREN', N'Yurtdışı Şube'),
        (N'KOSOVA', N'PEJA', N'Yurtdışı Şube'),
        (N'KOSOVA', N'FERİZAJ', N'Yurtdışı Şube'),
        (N'BULGARİSTAN', N'YÖNETİCİLİK', N'Yurtdışı Şube'),
        (N'BULGARİSTAN', N'SOFYA', N'Yurtdışı Şube'),
        (N'BULGARİSTAN', N'FİLİBE', N'Yurtdışı Şube'),
        (N'BULGARİSTAN', N'VARNA', N'Yurtdışı Şube'),
        (N'BULGARİSTAN', N'KIRCAALİ', N'Yurtdışı Şube'),
        (N'IRAK', N'BAĞDAT', N'Yurtdışı Şube'),
        (N'IRAK', N'ERBİL', N'Yurtdışı Şube'),
        (N'YUNANİSTAN', N'ATİNA', N'Yurtdışı Şube'),
        (N'YUNANİSTAN', N'İSKEÇE', N'Yurtdışı Şube'),
        (N'YUNANİSTAN', N'GÜMÜLCİNE', N'Yurtdışı Şube'),
        (N'SUUDİ ARABİSTAN', N'CİDDE', N'Yurtdışı Şube'),
        (N'BAHREYN', N'MANAMA', N'Yurtdışı Şube'),
        (N'İRAN', N'TAHRAN', N'Yurtdışı Şube'),
        (N'KKTC', N'KKTC', N'Yurtdışı Şube'),
        (N'KKTC', N'Girne', N'Yurtdışı Şube'),
        (N'KKTC', N'Lefkoşa', N'Yurtdışı Şube'),
        (N'KKTC', N'Gazimağusa', N'Yurtdışı Şube'),
        (N'KKTC', N'Güzelyurt', N'Yurtdışı Şube'),
        (N'KKTC', N'Taşkınköy', N'Yurtdışı Şube'),
        (N'KKTC', N'Gönyeli', N'Yurtdışı Şube'),
        (N'KKTC', N'Karaoğlanoğlu', N'Yurtdışı Şube'),
        (N'KKTC', N'İskele', N'Yurtdışı Şube'),
        (N'ARNAVUTLUK', N'TİRAN', N'Yurtdışı Şube'),
        (N'ALMANYA', N'BERLIN', N'Yurtdışı İştirak'),
        (N'ALMANYA', N'DISBURG', N'Yurtdışı İştirak'),
        (N'ALMANYA', N'FRANKFURT', N'Yurtdışı İştirak'),
        (N'ALMANYA', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'ALMANYA', N'HAMBURG', N'Yurtdışı İştirak'),
        (N'ALMANYA', N'HANNOVER', N'Yurtdışı İştirak'),
        (N'ALMANYA', N'KÖLN', N'Yurtdışı İştirak'),
        (N'ALMANYA', N'MÜNİH', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'AHMET RECEPLİ', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'AZADLIG', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'BABEK', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'BAKÜ', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'GENCE', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'GUBA HİZMET NOKTASI', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'İÇERİŞEHİR', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'NAHÇIVAN', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'NEFCİLER ŞUBESİ', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'SAMED VURGUN', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'SEDEREK', N'Yurtdışı İştirak'),
        (N'AZERBAYCAN', N'SUMQAYT', N'Yurtdışı İştirak'),
        (N'BOSNA', N'BANJA LUKA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'BIHAC ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'BIJELJINA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Bratunac', N'Yurtdışı İştirak'),
        (N'BOSNA', N'BRCKO ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Cazin', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Čelić', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Derventa', N'Yurtdışı İştirak'),
        (N'BOSNA', N'DOBOJ OFİSİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'DOBRINJA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Donji Vakuf', N'Yurtdışı İştirak'),
        (N'BOSNA', N'FERHADIJA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'BOSNA', N'GORADZE ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Gračanica', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Hadžići', N'Yurtdışı İştirak'),
        (N'BOSNA', N'ILIDZA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'ILIJAŞ OFİSİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'JELAH ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Kakanj', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Konjic', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Lukavac', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Maglaj', N'Yurtdışı İştirak'),
        (N'BOSNA', N'MOSTAR ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'NOVİ GRAD ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Novi Travnik', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Sanski Most', N'Yurtdışı İştirak'),
        (N'BOSNA', N'SARAJEVO ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Široki Brijeg', N'Yurtdışı İştirak'),
        (N'BOSNA', N'SREBRENİCA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Srebrenik', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Teslić', N'Yurtdışı İştirak'),
        (N'BOSNA', N'TRAVNİK ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'TUZLA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Ustikolina', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Visoko', N'Yurtdışı İştirak'),
        (N'BOSNA', N'VOGOSCA ŞUBESİ', N'Yurtdışı İştirak'),
        (N'BOSNA', N'Zenica', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Batum', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Gldani', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Kutaisi', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Marneuli', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Tiflis', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Tsereteli', N'Yurtdışı İştirak'),
        (N'GÜRCİSTAN', N'Varketeli', N'Yurtdışı İştirak'),
        (N'KARADAĞ', N'BAR', N'Yurtdışı İştirak'),
        (N'KARADAĞ', N'BUDVA', N'Yurtdışı İştirak'),
        (N'KARADAĞ', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'KARADAĞ', N'PODGORITSA', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'AKTAU', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'ALMATY', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'ALMATY-2', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'ASTANA', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'ATIRAU', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'ÇİMENT', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'KARAGANDİ', N'Yurtdışı İştirak'),
        (N'KAZAKİSTAN', N'TÜRKİSTAN', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'ANDİCAN', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'BUHARA', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'FERGANA', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'KURUMSAL ŞUBE', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'OPERU', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'SEMERKAND', N'Yurtdışı İştirak'),
        (N'ÖZBEKİSTAN', N'YUNUSABAD', N'Yurtdışı İştirak'),
        (N'RUSYA', N'Genel Müdürlük', N'Yurtdışı İştirak'),
        (N'TÜRKMENİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak')
                    ) AS v(CountryName, Branch, Class)
                    INNER JOIN dbo.Countries c ON c.Name = v.CountryName;
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807141454_AddLocationsTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807141454_AddLocationsTable', N'9.0.18');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    ALTER TABLE [dbo].[Servers] DROP CONSTRAINT [FK_Servers_Applications_ApplicationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    DROP INDEX [IX_Servers_ApplicationId] ON [dbo].[Servers];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    CREATE TABLE [dbo].[ServerEndpoints] (
        [Id] int NOT NULL IDENTITY,
        [ServerId] int NOT NULL,
        [IpAddress] nvarchar(50) NULL,
        [Port] int NULL,
        [ApplicationId] int NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_ServerEndpoints] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ServerEndpoints_Applications_ApplicationId] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ServerEndpoints_Servers_ServerId] FOREIGN KEY ([ServerId]) REFERENCES [dbo].[Servers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    CREATE INDEX [IX_ServerEndpoints_ApplicationId] ON [dbo].[ServerEndpoints] ([ApplicationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    CREATE INDEX [IX_ServerEndpoints_ServerId] ON [dbo].[ServerEndpoints] ([ServerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN

                    INSERT INTO dbo.ServerEndpoints (ServerId, IpAddress, Port, ApplicationId, CreatedAt)
                    SELECT Id, IpAddress, Port, ApplicationId, SYSUTCDATETIME()
                    FROM dbo.Servers
                    WHERE IpAddress IS NOT NULL OR Port IS NOT NULL OR ApplicationId IS NOT NULL;
                
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[dbo].[Servers]') AND [c].[name] = N'ApplicationId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Servers] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [dbo].[Servers] DROP COLUMN [ApplicationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[dbo].[Servers]') AND [c].[name] = N'IpAddress');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Servers] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [dbo].[Servers] DROP COLUMN [IpAddress];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[dbo].[Servers]') AND [c].[name] = N'Port');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Servers] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [dbo].[Servers] DROP COLUMN [Port];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    ALTER TABLE [dbo].[Servers] ADD [HostPhysicalDeviceId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[dbo].[Servers]') AND [c].[name] = N'Location');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Servers] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [dbo].[Servers] ALTER COLUMN [Location] nvarchar(255) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    CREATE INDEX [IX_Servers_HostPhysicalDeviceId] ON [dbo].[Servers] ([HostPhysicalDeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    ALTER TABLE [dbo].[Servers] ADD CONSTRAINT [FK_Servers_PhysicalDevices_HostPhysicalDeviceId] FOREIGN KEY ([HostPhysicalDeviceId]) REFERENCES [dbo].[PhysicalDevices] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810072801_SplitServerIntoHostAndEndpoints'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810072801_SplitServerIntoHostAndEndpoints', N'9.0.18');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810084227_AddLocationCategory'
)
BEGIN
    ALTER TABLE [dbo].[Servers] ADD [LocationCategory] nvarchar(20) NOT NULL DEFAULT N'Local';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810084227_AddLocationCategory'
)
BEGIN
    ALTER TABLE [dbo].[PhysicalDevices] ADD [LocationCategory] nvarchar(20) NOT NULL DEFAULT N'Local';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260810084227_AddLocationCategory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260810084227_AddLocationCategory', N'9.0.18');
END;

COMMIT;
GO

