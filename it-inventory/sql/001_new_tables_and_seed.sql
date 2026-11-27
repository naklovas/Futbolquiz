IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Countries] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Code] nvarchar(20) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Countries] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[DeviceCategories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_DeviceCategories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Circuits] (
        [Id] int NOT NULL IDENTITY,
        [CountryId] int NOT NULL,
        [CircuitType] nvarchar(100) NOT NULL,
        [CircuitCapacity] nvarchar(50) NULL,
        [Provider] nvarchar(150) NULL,
        [Branch] nvarchar(150) NULL,
        [Location] nvarchar(255) NOT NULL,
        [StartDate] datetime2 NULL,
        [EndDate] datetime2 NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Circuits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Circuits_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Licenses] (
        [Id] int NOT NULL IDENTITY,
        [CountryId] int NOT NULL,
        [LicenseName] nvarchar(255) NOT NULL,
        [VendorSupplier] nvarchar(150) NULL,
        [Branch] nvarchar(150) NULL,
        [Location] nvarchar(255) NOT NULL,
        [SupportStartDate] datetime2 NULL,
        [SupportEndDate] datetime2 NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Licenses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Licenses_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[DeviceProfileCatalog] (
        [Id] int NOT NULL IDENTITY,
        [ProfileName] nvarchar(150) NOT NULL,
        [CategoryId] int NULL,
        CONSTRAINT [PK_DeviceProfileCatalog] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeviceProfileCatalog_DeviceCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[DeviceCategories] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[PhysicalDevices] (
        [Id] int NOT NULL IDENTITY,
        [CountryId] int NOT NULL,
        [CategoryId] int NOT NULL,
        [DeviceProfileId] int NULL,
        [SourceZiraatYdId] int NULL,
        [DeviceName] nvarchar(255) NOT NULL,
        [Brand] nvarchar(100) NULL,
        [Model] nvarchar(150) NULL,
        [ApplianceType] nvarchar(20) NOT NULL,
        [SoftwareVersion] nvarchar(150) NULL,
        [SerialNo] nvarchar(150) NULL,
        [IpAddress] nvarchar(50) NULL,
        [MgmtIp] nvarchar(50) NULL,
        [Branch] nvarchar(150) NULL,
        [Location] nvarchar(255) NOT NULL,
        [VendorSupplier] nvarchar(150) NULL,
        [LicenceInfo] nvarchar(150) NULL,
        [StartOfSupportDate] datetime2 NULL,
        [EndOfSupportDate] datetime2 NULL,
        [EndOfLifeDate] datetime2 NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_PhysicalDevices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PhysicalDevices_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PhysicalDevices_DeviceCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[DeviceCategories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PhysicalDevices_DeviceProfileCatalog_DeviceProfileId] FOREIGN KEY ([DeviceProfileId]) REFERENCES [dbo].[DeviceProfileCatalog] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE TABLE [dbo].[Servers] (
        [Id] int NOT NULL IDENTITY,
        [CountryId] int NOT NULL,
        [DeviceProfileId] int NULL,
        [SourceZiraatYdId] int NULL,
        [HostName] nvarchar(255) NOT NULL,
        [ApplianceType] nvarchar(20) NOT NULL,
        [IpAddress] nvarchar(50) NULL,
        [OperatingSystem] nvarchar(255) NULL,
        [Brand] nvarchar(100) NULL,
        [Model] nvarchar(150) NULL,
        [SerialNo] nvarchar(150) NULL,
        [VendorSupplier] nvarchar(150) NULL,
        [Port] int NULL,
        [Branch] nvarchar(150) NULL,
        [Location] nvarchar(255) NOT NULL,
        [StartOfSupportDate] datetime2 NULL,
        [EndOfSupportDate] datetime2 NULL,
        [EndOfLifeDate] datetime2 NULL,
        [Notes] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Servers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Servers_Countries_CountryId] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Countries] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Servers_DeviceProfileCatalog_DeviceProfileId] FOREIGN KEY ([DeviceProfileId]) REFERENCES [dbo].[DeviceProfileCatalog] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[DeviceCategories]'))
        SET IDENTITY_INSERT [dbo].[DeviceCategories] ON;
    EXEC(N'INSERT INTO [dbo].[DeviceCategories] ([Id], [Name])
    VALUES (1, N''Sunucu''),
    (2, N''Ağ Cihazı''),
    (3, N''Güvenlik''),
    (4, N''Ses/Görüntü''),
    (5, N''Depolama''),
    (6, N''Yazıcı''),
    (7, N''İstemci''),
    (8, N''Sanallaştırma''),
    (9, N''Güç/Altyapı''),
    (10, N''Diğer'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[dbo].[DeviceCategories]'))
        SET IDENTITY_INSERT [dbo].[DeviceCategories] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CategoryId', N'ProfileName') AND [object_id] = OBJECT_ID(N'[dbo].[DeviceProfileCatalog]'))
        SET IDENTITY_INSERT [dbo].[DeviceProfileCatalog] ON;
    EXEC(N'INSERT INTO [dbo].[DeviceProfileCatalog] ([Id], [CategoryId], [ProfileName])
    VALUES (14, NULL, N''NULL''),
    (1, 5, N''Veri Depolama (NAS)''),
    (2, 3, N''Güvenlik Kameraları (CCTV / NVR)''),
    (3, 1, N''Sunucu / Appliance (Linux)''),
    (4, 4, N''IP Telefon (VoIP)''),
    (5, 2, N''Kablosuz Ağ (Access Point)''),
    (6, 2, N''Ağ Cihazı (SAN Switch)''),
    (7, 5, N''Veri Depolama (Storage)''),
    (8, 5, N''Veri Depolama (Storage Server)''),
    (9, 5, N''Veri Depolama (Storage / NAS)''),
    (10, 2, N''Endüstriyel Ağ Geçidi (IoT)''),
    (11, 1, N''Sunucu Yönetim Kartı (OOB / Console)''),
    (12, 8, N''Sanallaştırma (Container Host)''),
    (13, 2, N''Yük Dengeleyici (Load Balancer)''),
    (15, 2, N''Ağ Cihazı (Router)''),
    (16, 6, N''Yazıcı (Printer / Print Server)''),
    (17, 1, N''Bütünleşik Sistem (HCI)''),
    (18, 1, N''Sunucu Yönetim (Console Server / OOB)''),
    (19, 2, N''Ağ Servisleri (SD-WAN)''),
    (20, 8, N''Sanallaştırma (Yönetim Sunucusu)''),
    (21, 9, N''UPS / Güç Yönetimi''),
    (22, 1, N''Sunucu (Linux/SAP)''),
    (23, 1, N''Sunucu (Linux)''),
    (24, 1, N''Sunucu Yönetim (Console Server)''),
    (25, 1, N''Sunucu (Unix)''),
    (26, 3, N''Güvenlik / Ağ Cihazı''),
    (27, 2, N''Ağ Cihazı (Switch / Router)''),
    (28, 3, N''Güvenlik (Firewall)''),
    (29, 4, N''IP Telefon / Santral''),
    (30, 2, N''Ağ Cihazı (Genel)''),
    (31, 2, N''Ağ Cihazı (Switch)''),
    (32, 6, N''Yazıcı (Printer)''),
    (33, 1, N''Sunucu Yönetim Kartı (OOB)''),
    (34, 6, N''Yazıcı (Print Server)''),
    (35, 1, N''Sunucu / Appliance (Linux/IoT)''),
    (36, 8, N''Sanallaştırma (Hypervisor)''),
    (37, 3, N''Güvenlik Kameraları (CCTV)''),
    (38, 1, N''Sunucu (Windows)''),
    (39, 4, N''Medya Oynatıcı / Akıllı Ekran''),
    (40, 2, N''Ağ Servisleri (DDI)''),
    (41, 7, N''İstemci Bilgisayar (Workstation)''),
    (42, 4, N''IP Telefon (Analog Gateway)'');
    INSERT INTO [dbo].[DeviceProfileCatalog] ([Id], [CategoryId], [ProfileName])
    VALUES (43, 3, N''Güvenlik (Appliance)''),
    (44, 7, N''İstemci Bilgisayar (Workstation / Medya)''),
    (45, 1, N''Sunucu (Unix/Mainframe)''),
    (46, 2, N''Ağ Cihazı (Switch / HCI)''),
    (47, 3, N''Ağ Cihazı (Router / Firewall)'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CategoryId', N'ProfileName') AND [object_id] = OBJECT_ID(N'[dbo].[DeviceProfileCatalog]'))
        SET IDENTITY_INSERT [dbo].[DeviceProfileCatalog] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Circuits_CountryId] ON [dbo].[Circuits] ([CountryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Countries_Name] ON [dbo].[Countries] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeviceCategories_Name] ON [dbo].[DeviceCategories] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceProfileCatalog_CategoryId] ON [dbo].[DeviceProfileCatalog] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeviceProfileCatalog_ProfileName] ON [dbo].[DeviceProfileCatalog] ([ProfileName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Licenses_CountryId] ON [dbo].[Licenses] ([CountryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PhysicalDevices_CategoryId] ON [dbo].[PhysicalDevices] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PhysicalDevices_CountryId] ON [dbo].[PhysicalDevices] ([CountryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PhysicalDevices_DeviceProfileId] ON [dbo].[PhysicalDevices] ([DeviceProfileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Servers_CountryId] ON [dbo].[Servers] ([CountryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Servers_DeviceProfileId] ON [dbo].[Servers] ([DeviceProfileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805082548_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805082548_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

