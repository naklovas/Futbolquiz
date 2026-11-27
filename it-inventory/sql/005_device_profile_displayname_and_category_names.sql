BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    ALTER TABLE [dbo].[DeviceProfileCatalog] ADD [DisplayName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Server''
    WHERE [Id] = 1;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Network Device''
    WHERE [Id] = 2;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Security''
    WHERE [Id] = 3;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Audio/Video''
    WHERE [Id] = 4;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Storage''
    WHERE [Id] = 5;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Printer''
    WHERE [Id] = 6;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Client''
    WHERE [Id] = 7;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Virtualization''
    WHERE [Id] = 8;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Power/Infrastructure''
    WHERE [Id] = 9;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceCategories] SET [Name] = N''Other''
    WHERE [Id] = 10;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Data Storage (NAS)''
    WHERE [Id] = 1;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Security Cameras (CCTV / NVR)''
    WHERE [Id] = 2;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server / Appliance (Linux)''
    WHERE [Id] = 3;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''IP Phone (VoIP)''
    WHERE [Id] = 4;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Wireless Network (Access Point)''
    WHERE [Id] = 5;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Device (SAN Switch)''
    WHERE [Id] = 6;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Data Storage (Storage)''
    WHERE [Id] = 7;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Data Storage (Storage Server)''
    WHERE [Id] = 8;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Data Storage (Storage / NAS)''
    WHERE [Id] = 9;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Industrial Gateway (IoT)''
    WHERE [Id] = 10;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server Management Card (OOB / Console)''
    WHERE [Id] = 11;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Virtualization (Container Host)''
    WHERE [Id] = 12;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Load Balancer''
    WHERE [Id] = 13;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Unmapped''
    WHERE [Id] = 14;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Device (Router)''
    WHERE [Id] = 15;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Printer (Printer / Print Server)''
    WHERE [Id] = 16;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Hyperconverged Infrastructure (HCI)''
    WHERE [Id] = 17;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server Management (Console Server / OOB)''
    WHERE [Id] = 18;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Services (SD-WAN)''
    WHERE [Id] = 19;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Virtualization (Management Server)''
    WHERE [Id] = 20;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''UPS / Power Management''
    WHERE [Id] = 21;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server (Linux/SAP)''
    WHERE [Id] = 22;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server (Linux)''
    WHERE [Id] = 23;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server Management (Console Server)''
    WHERE [Id] = 24;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server (Unix)''
    WHERE [Id] = 25;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Security / Network Device''
    WHERE [Id] = 26;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Device (Switch / Router)''
    WHERE [Id] = 27;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Security (Firewall)''
    WHERE [Id] = 28;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''IP Phone / PBX''
    WHERE [Id] = 29;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Device (General)''
    WHERE [Id] = 30;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Device (Switch)''
    WHERE [Id] = 31;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Printer''
    WHERE [Id] = 32;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server Management Card (OOB)''
    WHERE [Id] = 33;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Printer (Print Server)''
    WHERE [Id] = 34;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server / Appliance (Linux/IoT)''
    WHERE [Id] = 35;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Virtualization (Hypervisor)''
    WHERE [Id] = 36;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Security Cameras (CCTV)''
    WHERE [Id] = 37;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server (Windows)''
    WHERE [Id] = 38;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Media Player / Smart Display''
    WHERE [Id] = 39;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Services (DDI)''
    WHERE [Id] = 40;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Client Computer (Workstation)''
    WHERE [Id] = 41;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''IP Phone (Analog Gateway)''
    WHERE [Id] = 42;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Security (Appliance)''
    WHERE [Id] = 43;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Client Computer (Workstation / Media)''
    WHERE [Id] = 44;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Server (Unix/Mainframe)''
    WHERE [Id] = 45;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Device (Switch / HCI)''
    WHERE [Id] = 46;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    EXEC(N'UPDATE [dbo].[DeviceProfileCatalog] SET [DisplayName] = N''Network Device (Router / Firewall)''
    WHERE [Id] = 47;
    SELECT @@ROWCOUNT');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806081136_AddDeviceProfileDisplayName'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260806081136_AddDeviceProfileDisplayName', N'8.0.11');
END;
GO

COMMIT;
GO

