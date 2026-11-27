-- Corrective script for databases that already ran the earlier versions of
-- 004_test_countries_users.sql / 008_test_inventory_data.sql:
--   1) Sets the correct DisplayName for the Germany/Bulgaria test countries
--      (they were showing as plain "Germany" / "Bulgaria" instead of the
--      official names).
--   2) Removes China entirely: it was added by mistake and is not one of
--      Ziraat Bank's actual overseas markets. Deletes all China test rows
--      from PhysicalDevices, Licenses and Circuits, then the China row
--      itself from Countries.
-- Idempotent; safe to run multiple times (and safe to run even if China/the
-- old DisplayName values were never present).

UPDATE dbo.Countries SET DisplayName = N'ZiraatBank AG International' WHERE Name = N'Germany';
UPDATE dbo.Countries SET DisplayName = N'ZiraatBank Bulgaria' WHERE Name = N'Bulgaria';
GO

DELETE pd
FROM dbo.PhysicalDevices pd
JOIN dbo.Countries c ON pd.CountryId = c.Id
WHERE c.Name = N'China';

DELETE l
FROM dbo.Licenses l
JOIN dbo.Countries c ON l.CountryId = c.Id
WHERE c.Name = N'China';

DELETE ci
FROM dbo.Circuits ci
JOIN dbo.Countries c ON ci.CountryId = c.Id
WHERE c.Name = N'China';

DELETE FROM dbo.Countries WHERE Name = N'China';
GO
