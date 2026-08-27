-- Corrective script: an earlier version of 004_test_countries_users.sql created
-- separate test-only Countries rows named 'Germany' and 'Bulgaria', instead of
-- reusing the real Countries rows ('Almanya' / 'Bulgaristan') that already exist
-- and match Ziraat_YD.RepositoryName. Once both rows ended up with similar
-- DisplayNames ("ZiraatBank AG International" twice, "Bulgaria" twice), every
-- country dropdown in the app showed confusing duplicate-looking entries.
--
-- This script consolidates everything onto the single real Countries row per
-- country and removes the redundant test-only rows entirely:
--   1) Sets the correct DisplayName on the real Almanya/Bulgaristan rows.
--   2) Re-points any PhysicalDevices/Servers/Licenses/Circuits rows that were
--      created under the test-only 'Germany'/'Bulgaria' countries so they now
--      belong to the real 'Almanya'/'Bulgaristan' rows (no data is lost).
--   3) Points germany_test/germany_view_test/bulgaria_test at the real
--      countries (RepositoryName), so Device Pool and the CRUD grids agree.
--   4) Deletes the now-unused test-only 'Germany'/'Bulgaria' Countries rows.
-- Idempotent; safe to run multiple times (and safe even if the old test-only
-- countries were never created).

DECLARE @AlmanyaId INT = (SELECT Id FROM dbo.Countries WHERE Name = N'Almanya');
DECLARE @BulgaristanId INT = (SELECT Id FROM dbo.Countries WHERE Name = N'Bulgaristan');
DECLARE @OldGermanyId INT = (SELECT Id FROM dbo.Countries WHERE Name = N'Germany');
DECLARE @OldBulgariaId INT = (SELECT Id FROM dbo.Countries WHERE Name = N'Bulgaria');

UPDATE dbo.Countries SET DisplayName = N'ZiraatBank AG International' WHERE Id = @AlmanyaId;
UPDATE dbo.Countries SET DisplayName = N'ZiraatBank Bulgaria' WHERE Id = @BulgaristanId;

IF @OldGermanyId IS NOT NULL AND @AlmanyaId IS NOT NULL
BEGIN
    UPDATE dbo.PhysicalDevices SET CountryId = @AlmanyaId WHERE CountryId = @OldGermanyId;
    UPDATE dbo.Servers SET CountryId = @AlmanyaId WHERE CountryId = @OldGermanyId;
    UPDATE dbo.Licenses SET CountryId = @AlmanyaId WHERE CountryId = @OldGermanyId;
    UPDATE dbo.Circuits SET CountryId = @AlmanyaId WHERE CountryId = @OldGermanyId;
END

IF @OldBulgariaId IS NOT NULL AND @BulgaristanId IS NOT NULL
BEGIN
    UPDATE dbo.PhysicalDevices SET CountryId = @BulgaristanId WHERE CountryId = @OldBulgariaId;
    UPDATE dbo.Servers SET CountryId = @BulgaristanId WHERE CountryId = @OldBulgariaId;
    UPDATE dbo.Licenses SET CountryId = @BulgaristanId WHERE CountryId = @OldBulgariaId;
    UPDATE dbo.Circuits SET CountryId = @BulgaristanId WHERE CountryId = @OldBulgariaId;
END

UPDATE dbo.YDUsers SET RepositoryName = N'Almanya' WHERE Username IN (N'germany_test', N'germany_view_test');
UPDATE dbo.YDUsers SET RepositoryName = N'Bulgaristan' WHERE Username = N'bulgaria_test';

DELETE FROM dbo.Countries WHERE Name = N'Germany';
DELETE FROM dbo.Countries WHERE Name = N'Bulgaria';
GO
