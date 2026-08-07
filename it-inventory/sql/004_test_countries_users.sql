-- Optional test data for local/dev testing of country-scoped access.
-- Requires the app's "Test Login" mode to be enabled (appsettings.json -> TestLogin:Enabled = true).
-- These users do NOT need a real AD account: they sign in via the "Log In as Test User"
-- button on the login page using the shared TestLogin:Password (default "12345").
--
-- IMPORTANT: this reuses the REAL Countries rows (Name = 'Almanya' / 'Bulgaristan' -- the
-- ones that match Ziraat_YD.RepositoryName for Device Pool) instead of creating separate
-- English-named "Germany"/"Bulgaria" countries. An earlier version of this script created
-- separate test-only countries, which caused duplicate-looking entries in every country
-- dropdown (e.g. "ZiraatBank AG International" appearing twice). Do not reintroduce that.
-- Idempotent; safe to run multiple times.

IF NOT EXISTS (SELECT 1 FROM dbo.Countries WHERE Name = N'Almanya')
    INSERT INTO dbo.Countries (Name, DisplayName, Code, IsActive, CreatedAt)
    VALUES (N'Almanya', N'ZiraatBank AG International', N'DE', 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.Countries WHERE Name = N'Bulgaristan')
    INSERT INTO dbo.Countries (Name, DisplayName, Code, IsActive, CreatedAt)
    VALUES (N'Bulgaristan', N'ZiraatBank Bulgaria', N'BG', 1, SYSUTCDATETIME());

UPDATE dbo.Countries SET DisplayName = N'ZiraatBank AG International' WHERE Name = N'Almanya' AND (DisplayName IS NULL OR DisplayName <> N'ZiraatBank AG International');
UPDATE dbo.Countries SET DisplayName = N'ZiraatBank Bulgaria' WHERE Name = N'Bulgaristan' AND (DisplayName IS NULL OR DisplayName <> N'ZiraatBank Bulgaria');

IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'germany_test')
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'germany_test', N'Germany Test User', NULL, 1, SYSUTCDATETIME(), N'Almanya');

IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'bulgaria_test')
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'bulgaria_test', N'Bulgaria Test User', NULL, 1, SYSUTCDATETIME(), N'Bulgaristan');

IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'germany_view_test')
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'germany_view_test', N'Germany View-Only Test User', NULL, 1, SYSUTCDATETIME(), N'Almanya');

-- Make sure existing rows (created by an earlier version of this script) point at the
-- real countries too, in case this is re-run on a database that already has these users.
UPDATE dbo.YDUsers SET RepositoryName = N'Almanya' WHERE Username IN (N'germany_test', N'germany_view_test') AND RepositoryName <> N'Almanya';
UPDATE dbo.YDUsers SET RepositoryName = N'Bulgaristan' WHERE Username = N'bulgaria_test' AND RepositoryName <> N'Bulgaristan';

-- country_manager: can view, add and edit records only within their own country.
INSERT INTO dbo.YDUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.YDUsers u
CROSS JOIN dbo.YDRoles r
WHERE u.Username = N'germany_test' AND r.RoleName = N'country_manager'
AND NOT EXISTS (SELECT 1 FROM dbo.YDUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

INSERT INTO dbo.YDUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.YDUsers u
CROSS JOIN dbo.YDRoles r
WHERE u.Username = N'bulgaria_test' AND r.RoleName = N'country_manager'
AND NOT EXISTS (SELECT 1 FROM dbo.YDUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

-- country_view_only: can only view records within their own country; no add/edit/delete/import.
INSERT INTO dbo.YDUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.YDUsers u
CROSS JOIN dbo.YDRoles r
WHERE u.Username = N'germany_view_test' AND r.RoleName = N'country_view_only'
AND NOT EXISTS (SELECT 1 FROM dbo.YDUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);
GO
