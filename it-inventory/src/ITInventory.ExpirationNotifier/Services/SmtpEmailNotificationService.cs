using System.Net;
using System.Net.Mail;
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
    private readonly int _upcomingWindowDays;
    private readonly string? _logoPath;
    private const string LogoCid = "logo";

    public SmtpEmailNotificationService(IConfiguration config, ILogger<SmtpEmailNotificationService> logger)
    {
        _logger = logger;
        _host = config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        _port = config.GetValue<int?>("Smtp:Port") ?? 25;
        _enableSsl = config.GetValue<bool?>("Smtp:EnableSsl") ?? false;
        _username = config["Smtp:Username"] ?? string.Empty;
        _password = config["Smtp:Password"] ?? string.Empty;
        _fromAddress = config["Smtp:FromAddress"] ?? throw new InvalidOperationException("Smtp:FromAddress is not configured.");
        _fromDisplayName = config["Smtp:FromDisplayName"] ?? "IT Inventory System";
        _upcomingWindowDays = config.GetValue<int?>("ExpirationNotifier:UpcomingWindowDays") ?? 90;

        // Drop logo.png/logo.jpg next to the exe (see the .csproj's CopyToOutputDirectory
        // entries) and it's picked up automatically -- no config needed. Without it, the
        // header just falls back to text-only.
        _logoPath = new[] { "logo.png", "logo.jpg" }
            .Select(name => Path.Combine(AppContext.BaseDirectory, name))
            .FirstOrDefault(File.Exists);
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
        var html = EmailTemplateBuilder.BuildHtml(
            group.CountryName,
            _upcomingWindowDays,
            group.ExpiredItems,
            group.UpcomingItems,
            _logoPath != null ? LogoCid : null);

        var message = new MailMessage
        {
            From = new MailAddress(_fromAddress, _fromDisplayName),
            Subject = $"[IT Inventory] {group.CountryName} - Expired/Upcoming Records ({group.ExpiredItems.Count} expired, {group.UpcomingItems.Count} upcoming)"
        };

        foreach (var recipient in group.Recipients)
        {
            message.To.Add(new MailAddress(recipient.Email, recipient.FullName));
        }

        // A logo needs an HTML body wrapped in an AlternateView with a LinkedResource
        // attached to it (MIME multipart/related) so the <img src="cid:logo"> tag in the
        // HTML has something to resolve against -- setting message.Body directly has no way
        // to carry that linked attachment, and classic Outlook desktop doesn't render data:
        // URI images at all, so this cid: approach is the only reliable one for Outlook.
        if (_logoPath != null)
        {
            var htmlView = AlternateView.CreateAlternateViewFromString(html, null, "text/html");
            htmlView.LinkedResources.Add(new LinkedResource(_logoPath) { ContentId = LogoCid });
            message.AlternateViews.Add(htmlView);
        }
        else
        {
            message.IsBodyHtml = true;
            message.Body = html;
        }

        return message;
    }
}
