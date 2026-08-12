namespace ITInventory.MailTemplatePreview;

// Mirrors ITInventory.ExpirationNotifier.Models.ExpiringItemNotification's shape
// (Category/Label/Name/ExpiresAt) on purpose: once the template looks right, porting
// EmailTemplateBuilder.cs over to the real project is close to copy-paste -- just swap this
// record for the real model type.
public record MockItem(string Category, string Label, string Name, DateTime ExpiresAt);
