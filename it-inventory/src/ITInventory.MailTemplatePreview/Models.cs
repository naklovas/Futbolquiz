namespace ITInventory.MailTemplatePreview;

// Mirrors ITInventory.ExpirationNotifier.Models.ExpiringItemNotification's shape
// (Category/Label/ExpirationType/Name/ExpiresAt) on purpose: once the template looks right,
// porting EmailTemplateBuilder.cs over to the real project is close to copy-paste -- just
// swap this record for the real model type (ExpirationType there is an enum; ToString() on
// it gives the same "License"/"EndOfSupport"/"EndOfLife" values used here).
public record MockItem(string Category, string Label, string ExpirationType, string Name, DateTime ExpiresAt);
