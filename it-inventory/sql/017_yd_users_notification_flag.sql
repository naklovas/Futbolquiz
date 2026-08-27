-- dbo.YDUsers migration'lara dahil edilmiyor (ExcludeFromMigrations, bkz. 003. script) --
-- bu yüzden yeni kolonu elle, idempotent olarak ekliyoruz.
--
-- ReceiveExpirationNotifications: ExpirationNotifier'in bu kullanıcıya süresi dolan/
-- dolmakta olan kayıtlar için mail atıp atmayacağını kontrol eder. Mevcut kullanıcılar
-- (ve varsayılan olarak yeni eklenenler) 1 (mail alır) ile başlar; admin ekranından
-- kullanıcı bazlı kapatılabilir.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[YDUsers]') AND name = 'ReceiveExpirationNotifications'
)
BEGIN
    ALTER TABLE [dbo].[YDUsers]
    ADD [ReceiveExpirationNotifications] bit NOT NULL
        CONSTRAINT [DF_YDUsers_ReceiveExpirationNotifications] DEFAULT (1);
END;
GO
