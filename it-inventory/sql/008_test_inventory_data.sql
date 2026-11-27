-- Test/demo inventory data for Germany and Bulgaria.
-- All device names and license names are prefixed with TEST- so they are
-- unmistakably test data; Notes also flags them. Safe to delete anytime
-- (e.g. DELETE FROM dbo.PhysicalDevices WHERE DeviceName LIKE N'TEST-%';
-- and the same pattern for dbo.Licenses / dbo.Circuits).
-- Idempotent; safe to run multiple times.

-- ================= Physical Devices =================
-- Germany
IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Almanya' AND pd.DeviceName = N'TEST-DE-FW-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-DE-FW-01', N'Fortinet', N'FortiGate 200F', 0, N'FortiOS 7.4.3', N'FGT200FDETEST01', N'10.50.1.1', N'10.50.1.254', N'Frankfurt HQ', N'Frankfurt HQ - Server Room', N'Fortinet Inc. (local reseller)', N'FortiCare Premium + FortiGuard UTP', '2023-01-15', '2026-09-15', '2028-06-30', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Almanya' AND cat.Name = N'Security';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Almanya' AND pd.DeviceName = N'TEST-DE-SW-CORE-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-DE-SW-CORE-01', N'Cisco', N'Catalyst 9300-48P', 0, N'IOS-XE 17.12.4', N'CAT9300DETEST01', N'10.50.1.2', N'10.50.1.253', N'Frankfurt HQ', N'Frankfurt HQ - Server Room', N'Cisco Systems (local reseller)', N'Cisco SmartNet 24x7x4', '2022-06-01', '2027-05-31', '2029-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Almanya' AND cat.Name = N'Network Device';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Almanya' AND pd.DeviceName = N'TEST-DE-RTR-EDGE-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-DE-RTR-EDGE-01', N'Cisco', N'ISR 4331', 0, N'IOS-XE 17.9.5', N'ISR4331DETEST01', N'10.50.0.1', N'10.50.0.254', N'Frankfurt HQ', N'Frankfurt HQ - Server Room', N'Cisco Systems (local reseller)', N'Cisco SmartNet 8x5xNBD', '2022-03-01', '2026-12-31', '2028-03-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Almanya' AND cat.Name = N'Network Device';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Almanya' AND pd.DeviceName = N'TEST-DE-NAS-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-DE-NAS-01', N'Synology', N'RS3621xs+', 0, N'DSM 7.2.1', N'SYN3621DETEST01', N'10.50.2.10', N'10.50.2.254', N'Frankfurt HQ', N'Frankfurt HQ - Server Room', N'Synology Inc. (local reseller)', N'Synology EW3 Extended Warranty', '2024-02-01', '2027-01-31', '2029-02-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Almanya' AND cat.Name = N'Storage';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Almanya' AND pd.DeviceName = N'TEST-DE-PRN-BRANCH-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-DE-PRN-BRANCH-01', N'HP', N'LaserJet Enterprise M507dn', 0, N'Firmware 2409A', N'HPM507DETEST01', N'10.50.3.20', NULL, N'Frankfurt HQ', N'Frankfurt HQ - Server Room', N'HP Inc. (local reseller)', N'HP Care Pack 3yr NBD', '2021-05-01', '2024-04-30', '2026-05-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Almanya' AND cat.Name = N'Printer';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Almanya' AND pd.DeviceName = N'TEST-DE-UPS-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-DE-UPS-01', N'APC', N'Smart-UPS SRT 10000VA', 0, N'Firmware UPS 15.1', N'APCSRTDETEST01', NULL, N'10.50.4.30', N'Frankfurt HQ', N'Frankfurt HQ - Server Room', N'Schneider Electric (local reseller)', N'APC WBEXTWAR3YR-SP-04', '2023-09-01', '2026-08-31', '2030-09-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Almanya' AND cat.Name = N'Power/Infrastructure';
GO

-- Bulgaria
IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND pd.DeviceName = N'TEST-BG-FW-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-BG-FW-01', N'Fortinet', N'FortiGate 200F', 0, N'FortiOS 7.4.3', N'FGT200FBGTEST01', N'10.51.1.1', N'10.51.1.254', N'Sofia Branch', N'Sofia Branch - Server Room', N'Fortinet Inc. (local reseller)', N'FortiCare Premium + FortiGuard UTP', '2023-01-15', '2026-10-01', '2028-06-30', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Bulgaristan' AND cat.Name = N'Security';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND pd.DeviceName = N'TEST-BG-SW-CORE-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-BG-SW-CORE-01', N'Cisco', N'Catalyst 9300-48P', 0, N'IOS-XE 17.12.4', N'CAT9300BGTEST01', N'10.51.1.2', N'10.51.1.253', N'Sofia Branch', N'Sofia Branch - Server Room', N'Cisco Systems (local reseller)', N'Cisco SmartNet 24x7x4', '2022-06-01', '2027-05-31', '2029-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Bulgaristan' AND cat.Name = N'Network Device';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND pd.DeviceName = N'TEST-BG-RTR-EDGE-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-BG-RTR-EDGE-01', N'Cisco', N'ISR 4331', 0, N'IOS-XE 17.9.5', N'ISR4331BGTEST01', N'10.51.0.1', N'10.51.0.254', N'Sofia Branch', N'Sofia Branch - Server Room', N'Cisco Systems (local reseller)', N'Cisco SmartNet 8x5xNBD', '2022-03-01', '2026-12-31', '2028-03-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Bulgaristan' AND cat.Name = N'Network Device';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND pd.DeviceName = N'TEST-BG-NAS-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-BG-NAS-01', N'Synology', N'RS3621xs+', 0, N'DSM 7.2.1', N'SYN3621BGTEST01', N'10.51.2.10', N'10.51.2.254', N'Sofia Branch', N'Sofia Branch - Server Room', N'Synology Inc. (local reseller)', N'Synology EW3 Extended Warranty', '2024-02-01', '2027-01-31', '2029-02-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Bulgaristan' AND cat.Name = N'Storage';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND pd.DeviceName = N'TEST-BG-PRN-BRANCH-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-BG-PRN-BRANCH-01', N'HP', N'LaserJet Enterprise M507dn', 0, N'Firmware 2409A', N'HPM507BGTEST01', N'10.51.3.20', NULL, N'Sofia Branch', N'Sofia Branch - Server Room', N'HP Inc. (local reseller)', N'HP Care Pack 3yr NBD', '2021-05-01', '2024-04-30', '2026-05-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Bulgaristan' AND cat.Name = N'Printer';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PhysicalDevices pd JOIN dbo.Countries c ON pd.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND pd.DeviceName = N'TEST-BG-UPS-01')
INSERT INTO dbo.PhysicalDevices (CountryId, CategoryId, DeviceName, Brand, Model, ApplianceType, SoftwareVersion, SerialNo, IpAddress, MgmtIp, Branch, Location, VendorSupplier, LicenceInfo, StartOfSupportDate, EndOfSupportDate, EndOfLifeDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, cat.Id, N'TEST-BG-UPS-01', N'APC', N'Smart-UPS SRT 10000VA', 0, N'Firmware UPS 15.1', N'APCSRTBGTEST01', NULL, N'10.51.4.30', N'Sofia Branch', N'Sofia Branch - Server Room', N'Schneider Electric (local reseller)', N'APC WBEXTWAR3YR-SP-04', '2023-09-01', '2026-08-31', '2030-09-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c CROSS JOIN dbo.DeviceCategories cat
WHERE c.Name = N'Bulgaristan' AND cat.Name = N'Power/Infrastructure';
GO

-- ================= Licenses =================
-- Germany
IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Almanya' AND l.LicenseName = N'TEST-DE-Windows-Server-Datacenter-License')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-DE-Windows-Server-Datacenter-License', N'Microsoft', N'Frankfurt HQ', N'Frankfurt HQ - IT Office', '2023-01-01', '2026-12-31', '2026-09-01', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Almanya' AND l.LicenseName = N'TEST-DE-FortiGate-UTP-Bundle-Support')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-DE-FortiGate-UTP-Bundle-Support', N'Fortinet Inc.', N'Frankfurt HQ', N'Frankfurt HQ - IT Office', '2023-01-15', '2027-01-14', '2027-01-14', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Almanya' AND l.LicenseName = N'TEST-DE-VMware-vSphere-Enterprise-Plus')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-DE-VMware-vSphere-Enterprise-Plus', N'Broadcom (VMware)', N'Frankfurt HQ', N'Frankfurt HQ - IT Office', '2024-04-01', '2027-03-31', '2027-03-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Almanya' AND l.LicenseName = N'TEST-DE-Veeam-Backup-and-Replication-Enterprise')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-DE-Veeam-Backup-and-Replication-Enterprise', N'Veeam Software', N'Frankfurt HQ', N'Frankfurt HQ - IT Office', '2024-06-01', '2026-11-30', '2026-11-30', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Almanya' AND l.LicenseName = N'TEST-DE-CrowdStrike-Falcon-Endpoint-Protection')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-DE-CrowdStrike-Falcon-Endpoint-Protection', N'CrowdStrike', N'Frankfurt HQ', N'Frankfurt HQ - IT Office', '2024-01-01', '2026-12-31', '2026-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

-- Bulgaria
IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND l.LicenseName = N'TEST-BG-Windows-Server-Datacenter-License')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-BG-Windows-Server-Datacenter-License', N'Microsoft', N'Sofia Branch', N'Sofia Branch - IT Office', '2023-01-01', '2026-12-31', '2026-10-10', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND l.LicenseName = N'TEST-BG-FortiGate-UTP-Bundle-Support')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-BG-FortiGate-UTP-Bundle-Support', N'Fortinet Inc.', N'Sofia Branch', N'Sofia Branch - IT Office', '2023-01-15', '2027-01-14', '2027-01-14', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND l.LicenseName = N'TEST-BG-VMware-vSphere-Enterprise-Plus')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-BG-VMware-vSphere-Enterprise-Plus', N'Broadcom (VMware)', N'Sofia Branch', N'Sofia Branch - IT Office', '2024-04-01', '2027-03-31', '2027-03-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND l.LicenseName = N'TEST-BG-Veeam-Backup-and-Replication-Enterprise')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-BG-Veeam-Backup-and-Replication-Enterprise', N'Veeam Software', N'Sofia Branch', N'Sofia Branch - IT Office', '2024-06-01', '2026-11-30', '2026-11-30', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Licenses l JOIN dbo.Countries c ON l.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND l.LicenseName = N'TEST-BG-CrowdStrike-Falcon-Endpoint-Protection')
INSERT INTO dbo.Licenses (CountryId, LicenseName, VendorSupplier, Branch, Location, SupportStartDate, SupportEndDate, ExpirationDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'TEST-BG-CrowdStrike-Falcon-Endpoint-Protection', N'CrowdStrike', N'Sofia Branch', N'Sofia Branch - IT Office', '2024-01-01', '2026-12-31', '2026-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO

-- ================= Circuits =================
-- Germany
IF NOT EXISTS (SELECT 1 FROM dbo.Circuits ci JOIN dbo.Countries c ON ci.CountryId = c.Id WHERE c.Name = N'Almanya' AND ci.CircuitType = N'Primary Internet Leased Line' AND ci.Provider = N'Deutsche Telekom')
INSERT INTO dbo.Circuits (CountryId, CircuitType, CircuitCapacity, Provider, Branch, Location, StartDate, EndDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'Primary Internet Leased Line', N'100 Mbps', N'Deutsche Telekom', N'TEST - Frankfurt HQ', N'Frankfurt HQ - Network Room', '2023-01-01', '2027-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Circuits ci JOIN dbo.Countries c ON ci.CountryId = c.Id WHERE c.Name = N'Almanya' AND ci.CircuitType = N'Backup Internet Line' AND ci.Provider = N'Vodafone Germany')
INSERT INTO dbo.Circuits (CountryId, CircuitType, CircuitCapacity, Provider, Branch, Location, StartDate, EndDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'Backup Internet Line', N'50 Mbps', N'Vodafone Germany', N'TEST - Frankfurt HQ', N'Frankfurt HQ - Network Room', '2023-06-01', '2026-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Circuits ci JOIN dbo.Countries c ON ci.CountryId = c.Id WHERE c.Name = N'Almanya' AND ci.CircuitType = N'Site-to-Site MPLS to HQ' AND ci.Provider = N'Deutsche Telekom')
INSERT INTO dbo.Circuits (CountryId, CircuitType, CircuitCapacity, Provider, Branch, Location, StartDate, EndDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'Site-to-Site MPLS to HQ', N'20 Mbps', N'Deutsche Telekom', N'TEST - Frankfurt HQ', N'Frankfurt HQ - Network Room', '2022-01-01', '2026-09-30', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Almanya';
GO

-- Bulgaria
IF NOT EXISTS (SELECT 1 FROM dbo.Circuits ci JOIN dbo.Countries c ON ci.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND ci.CircuitType = N'Primary Internet Leased Line' AND ci.Provider = N'A1 Bulgaria')
INSERT INTO dbo.Circuits (CountryId, CircuitType, CircuitCapacity, Provider, Branch, Location, StartDate, EndDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'Primary Internet Leased Line', N'100 Mbps', N'A1 Bulgaria', N'TEST - Sofia Branch', N'Sofia Branch - Network Room', '2023-01-01', '2027-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Circuits ci JOIN dbo.Countries c ON ci.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND ci.CircuitType = N'Backup Internet Line' AND ci.Provider = N'Vivacom')
INSERT INTO dbo.Circuits (CountryId, CircuitType, CircuitCapacity, Provider, Branch, Location, StartDate, EndDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'Backup Internet Line', N'50 Mbps', N'Vivacom', N'TEST - Sofia Branch', N'Sofia Branch - Network Room', '2023-06-01', '2026-12-31', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Circuits ci JOIN dbo.Countries c ON ci.CountryId = c.Id WHERE c.Name = N'Bulgaristan' AND ci.CircuitType = N'Site-to-Site MPLS to HQ' AND ci.Provider = N'A1 Bulgaria')
INSERT INTO dbo.Circuits (CountryId, CircuitType, CircuitCapacity, Provider, Branch, Location, StartDate, EndDate, Notes, CreatedAt, CreatedBy)
SELECT c.Id, N'Site-to-Site MPLS to HQ', N'20 Mbps', N'A1 Bulgaria', N'TEST - Sofia Branch', N'Sofia Branch - Network Room', '2022-01-01', '2026-09-30', N'Test data for pagination/demo purposes. Safe to delete.', SYSUTCDATETIME(), N'seed_script'
FROM dbo.Countries c
WHERE c.Name = N'Bulgaristan';
GO
