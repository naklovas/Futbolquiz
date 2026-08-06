-- Optional test data for local/dev testing of country-scoped access.
-- Requires the app's "Test Login" mode to be enabled (appsettings.json -> TestLogin:Enabled = true).
-- These users do NOT need a real AD account: they sign in via the "Log In as Test User"
-- button on the login page using the shared TestLogin:Password (default "12345").
-- Idempotent; safe to run multiple times.

IF NOT EXISTS (SELECT 1 FROM dbo.Countries WHERE Name = N'Germany')
    INSERT INTO dbo.Countries (Name, DisplayName, Code, IsActive, CreatedAt)
    VALUES (N'Germany', N'ZiraatBank AG International', N'DE', 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.Countries WHERE Name = N'Bulgaria')
    INSERT INTO dbo.Countries (Name, DisplayName, Code, IsActive, CreatedAt)
    VALUES (N'Bulgaria', N'ZiraatBank Bulgaria', N'BG', 1, SYSUTCDATETIME());

UPDATE dbo.Countries SET DisplayName = N'ZiraatBank AG International' WHERE Name = N'Germany' AND (DisplayName IS NULL OR DisplayName <> N'ZiraatBank AG International');
UPDATE dbo.Countries SET DisplayName = N'ZiraatBank Bulgaria' WHERE Name = N'Bulgaria' AND (DisplayName IS NULL OR DisplayName <> N'ZiraatBank Bulgaria');

IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'germany_test')
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'germany_test', N'Germany Test User', NULL, 1, SYSUTCDATETIME(), N'Germany');

IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'bulgaria_test')
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'bulgaria_test', N'Bulgaria Test User', NULL, 1, SYSUTCDATETIME(), N'Bulgaria');

IF NOT EXISTS (SELECT 1 FROM dbo.YDUsers WHERE Username = N'germany_view_test')
    INSERT INTO dbo.YDUsers (Username, FullName, Email, IsActive, CreatedAt, RepositoryName)
    VALUES (N'germany_view_test', N'Germany View-Only Test User', NULL, 1, SYSUTCDATETIME(), N'Germany');

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
