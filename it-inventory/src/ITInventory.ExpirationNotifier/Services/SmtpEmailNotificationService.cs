using System.Net;
using System.Net.Mail;
using System.Text;
using ITInventory.ExpirationNotifier.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ITInventory.ExpirationNotifier.Services;

/// <summary>
/// Sends through a plain SMTP relay (Smtp:* in appsettings.json) -- most internal relays
/// accept mail from a whitelisted server IP without authentication, so Username/Password
/// are optional; leave them blank to skip client.Credentials entirely.
/// </summary>
public class SmtpEmailNotificationService : IEmailNotificationService
{
    private readonly ILogger<SmtpEmailNotificationService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _enableSsl;
    private readonly string _username;
    private readonly string _password;
    private readonly string _fromAddress;
    private readonly string _fromDisplayName;

    public SmtpEmailNotificationService(IConfiguration config, ILogger<SmtpEmailNotificationService> logger)
    {
        _logger = logger;
        _host = config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        _port = config.GetValue<int?>("Smtp:Port") ?? 25;
        _enableSsl = config.GetValue<bool?>("Smtp:EnableSsl") ?? false;
        _username = config["Smtp:Username"] ?? string.Empty;
        _password = config["Smtp:Password"] ?? string.Empty;
        _fromAddress = config["Smtp:FromAddress"] ?? throw new InvalidOperationException("Smtp:FromAddress is not configured.");
        _fromDisplayName = config["Smtp:FromDisplayName"] ?? "IT Envanter Sistemi";
    }

    public async Task NotifyAsync(IReadOnlyList<CountryNotificationGroup> groups)
    {
        using var client = new SmtpClient(_host, _port) { EnableSsl = _enableSsl };
        if (!string.IsNullOrWhiteSpace(_username))
        {
            client.Credentials = new NetworkCredential(_username, _password);
        }

        foreach (var group in groups)
        {
            if (group.Recipients.Count == 0)
            {
                _logger.LogWarning("{Country}: {ExpiredCount} expired, {UpcomingCount} upcoming, but no recipient email is configured (no matching YDUsers.Email and no admin has one either).",
                    group.CountryName, group.ExpiredItems.Count, group.UpcomingItems.Count);
                continue;
            }

            using var message = BuildMessage(group);
            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Sent notification email for {Country} to {RecipientCount} recipient(s): {Recipients}.",
                    group.CountryName, group.Recipients.Count, string.Join(", ", group.Recipients.Select(r => r.Email)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification email for {Country}.", group.CountryName);
            }
        }
    }

    private MailMessage BuildMessage(CountryNotificationGroup group)
    {
        var message = new MailMessage
        {
            From = new MailAddress(_fromAddress, _fromDisplayName),
            Subject = $"[IT Envanter] {group.CountryName} - Suresi Dolan/Dolmakta Olan Kayitlar ({group.ExpiredItems.Count} dolmus, {group.UpcomingItems.Count} yaklasan)",
            IsBodyHtml = true,
            Body = BuildHtmlBody(group)
        };

        foreach (var recipient in group.Recipients)
        {
            message.To.Add(new MailAddress(recipient.Email, recipient.FullName));
        }

        return message;
    }

    private static string BuildHtmlBody(CountryNotificationGroup group)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body style='font-family: Segoe UI, Arial, sans-serif; font-size: 13px; color: #1e293b;'>");
        sb.Append($"<h3>{WebUtility.HtmlEncode(group.CountryName)} - IT Envanter Süresi Dolan/Dolmakta Olan Kayıtlar</h3>");

        AppendTable(sb, "Süresi Dolmuş (Expired)", group.ExpiredItems, "#dc2626");
        AppendTable(sb, "Yaklaşan (Upcoming)", group.UpcomingItems, "#d97706");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendTable(StringBuilder sb, string title, List<ExpiringItemNotification> items, string accentColor)
    {
        if (items.Count == 0) return;

        sb.Append($"<h4 style='color:{accentColor};'>{WebUtility.HtmlEncode(title)} ({items.Count})</h4>");
        sb.Append("<table style='border-collapse: collapse; width: 100%; margin-bottom: 16px;'>");
        sb.Append("<tr style='background:#f1f5f9;'>" +
                  "<th style='text-align:left; padding:4px 8px; border:1px solid #e2e8f0;'>Tür</th>" +
                  "<th style='text-align:left; padding:4px 8px; border:1px solid #e2e8f0;'>Kategori</th>" +
                  "<th style='text-align:left; padding:4px 8px; border:1px solid #e2e8f0;'>Ad</th>" +
                  "<th style='text-align:left; padding:4px 8px; border:1px solid #e2e8f0;'>Tarih</th></tr>");

        foreach (var item in items.OrderBy(i => i.ExpiresAt))
        {
            sb.Append("<tr>");
            sb.Append($"<td style='padding:4px 8px; border:1px solid #e2e8f0;'>{WebUtility.HtmlEncode(item.Label)}</td>");
            sb.Append($"<td style='padding:4px 8px; border:1px solid #e2e8f0;'>{item.ExpirationType}</td>");
            sb.Append($"<td style='padding:4px 8px; border:1px solid #e2e8f0;'>{WebUtility.HtmlEncode(item.Name)}</td>");
            sb.Append($"<td style='padding:4px 8px; border:1px solid #e2e8f0;'>{item.ExpiresAt:dd.MM.yyyy}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</table>");
    }
}
