using System.Net;
using System.Text;
using ITInventory.ExpirationNotifier.Models;

namespace ITInventory.ExpirationNotifier.Services;

/// <summary>
/// Table-based layout with every style inline -- no &lt;style&gt; block, no flexbox/grid.
/// Outlook desktop (Word rendering engine) and a lot of corporate webmail strip or ignore
/// both, so this is the only layout approach that reliably looks the same everywhere.
/// Ported from ITInventory.MailTemplatePreview once the design was approved there against
/// the real SMTP relay; only the item type changed (MockItem -> ExpiringItemNotification).
/// </summary>
public static class EmailTemplateBuilder
{
    private const string FontFamily = "'Segoe UI', Arial, sans-serif";

    /// <param name="logoCid">
    /// Content-ID of a logo image linked into the message (see SmtpEmailNotificationService's
    /// LinkedResource wiring) -- rendered as &lt;img src="cid:{logoCid}"&gt;. Null/empty skips
    /// the logo and falls back to the text-only header. Deliberately not a data: URI: classic
    /// Outlook desktop's Word rendering engine does not display data: URI images at all, so a
    /// linked cid: attachment is the only reliable way to embed an image in this kind of email.
    /// </param>
    public static string BuildHtml(string countryName, int windowDays, IReadOnlyList<ExpiringItemNotification> expired, IReadOnlyList<ExpiringItemNotification> upcoming, string? logoCid = null)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;

        sb.Append("<!DOCTYPE html><html><head>");
        // Tells dark-mode-aware clients (new Outlook, Outlook.com, Apple Mail, Gmail app)
        // this email is light-only and was designed on purpose -- without this, several of
        // them auto-invert/recolor the HTML. Classic Outlook desktop ignores this meta and
        // always does its own thing, which is why every color below is also set via the
        // bgcolor HTML attribute, not just inline CSS -- Outlook's Word engine respects
        // bgcolor more reliably than CSS background during its own dark-mode pass.
        sb.Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">");
        sb.Append("</head>");
        sb.Append("<body style=\"margin:0; padding:0; background:#f1f5f9; font-family:").Append(FontFamily).Append(";\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" bgcolor=\"#f1f5f9\" style=\"background:#f1f5f9; padding:24px 0;\"><tr><td align=\"center\">");
        sb.Append("<table role=\"presentation\" width=\"640\" cellpadding=\"0\" cellspacing=\"0\" bgcolor=\"#ffffff\" style=\"background:#ffffff; border-radius:8px; overflow:hidden;\">");

        // Header -- light background so the bank's regular (non-reversed) logo works as-is.
        sb.Append("<tr><td bgcolor=\"#f8fafc\" style=\"background:#f8fafc; padding:20px 32px; border-bottom:1px solid #e2e8f0;\">");
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
        if (!string.IsNullOrEmpty(logoCid))
        {
            sb.Append($"<td style=\"padding-right:16px;\"><img src=\"cid:{logoCid}\" alt=\"Ziraat\" height=\"48\" style=\"display:block; border:0; height:48px;\"></td>");
        }
        sb.Append("<td>");
        sb.Append("<div style=\"color:#0f172a; font-size:20px; font-weight:600;\">IT Inventory System</div>");
        sb.Append($"<div style=\"color:#64748b; font-size:13px; margin-top:4px;\">Expiration Notice &mdash; {WebUtility.HtmlEncode(countryName)}</div>");
        sb.Append("</td>");
        sb.Append("</tr></table>");
        sb.Append("</td></tr>");

        // Summary + badges
        sb.Append("<tr><td bgcolor=\"#ffffff\" style=\"background:#ffffff; padding:24px 32px 8px 32px;\">");
        sb.Append($"<p style=\"margin:0; color:#334155; font-size:14px; line-height:1.6;\">This is an automated summary of IT assets that have expired or are expiring within the next {windowDays} days.</p>");
        // Nested table instead of two <span>s with margin-right: Outlook's Word rendering
        // engine is unreliable about honoring margin on inline-block elements but always
        // honors real table cells, so the gap is a spacer <td> instead of a CSS margin.
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:16px;\"><tr>");
        sb.Append($"<td bgcolor=\"#fee2e2\" style=\"background:#fee2e2; border-radius:999px;\"><div style=\"color:#dc2626; font-size:13px; font-weight:600; padding:4px 12px; white-space:nowrap;\">{expired.Count} Expired</div></td>");
        sb.Append("<td style=\"width:16px; font-size:1px; line-height:1px;\">&nbsp;</td>");
        sb.Append($"<td bgcolor=\"#fef3c7\" style=\"background:#fef3c7; border-radius:999px;\"><div style=\"color:#d97706; font-size:13px; font-weight:600; padding:4px 12px; white-space:nowrap;\">{upcoming.Count} Upcoming</div></td>");
        sb.Append("</tr></table>");
        sb.Append("</td></tr>");

        if (expired.Count > 0)
        {
            AppendSection(sb, "Expired", "#dc2626", expired, now, isExpired: true);
        }

        if (upcoming.Count > 0)
        {
            AppendSection(sb, "Upcoming", "#d97706", upcoming, now, isExpired: false);
        }

        // Footer
        sb.Append("<tr><td bgcolor=\"#f8fafc\" style=\"padding:20px 32px; background:#f8fafc; border-top:1px solid #e2e8f0;\">");
        sb.Append("<p style=\"margin:0; color:#94a3b8; font-size:12px;\">This is an automated message from IT Inventory System. Please do not reply to this email.</p>");
        sb.Append($"<p style=\"margin:4px 0 0 0; color:#94a3b8; font-size:12px;\">Generated on {now:dd MMM yyyy HH:mm}</p>");
        sb.Append("</td></tr>");

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, string accentColor, IReadOnlyList<ExpiringItemNotification> items, DateTime now, bool isExpired)
    {
        sb.Append("<tr><td bgcolor=\"#ffffff\" style=\"background:#ffffff; padding:16px 32px 8px 32px;\">");
        sb.Append($"<div style=\"color:{accentColor}; font-size:14px; font-weight:600; margin-bottom:8px;\">{WebUtility.HtmlEncode(title)} ({items.Count})</div>");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse; font-size:13px;\">");

        sb.Append("<tr>");
        AppendHeaderCell(sb, "Category", "left");
        AppendHeaderCell(sb, "Name", "left");
        AppendHeaderCell(sb, "Type", "left");
        AppendHeaderCell(sb, "Expiration Type", "left");
        AppendHeaderCell(sb, isExpired ? "Expired On" : "Expires On", "left");
        AppendHeaderCell(sb, isExpired ? "Overdue" : "Remaining", "right");
        sb.Append("</tr>");

        var rowIndex = 0;
        foreach (var item in items.OrderBy(i => i.ExpiresAt))
        {
            var rowBg = rowIndex % 2 == 0 ? "#ffffff" : "#f8fafc";
            var days = Math.Abs((item.ExpiresAt.Date - now.Date).Days);
            var daysLabel = days == 0 ? "today" : days == 1 ? "1 day" : $"{days} days";
            var (badgeBg, badgeFg) = CategoryColors(item.Category);
            var expirationType = item.ExpirationType.ToString();
            var (expBadgeBg, expBadgeFg) = ExpirationTypeColors(expirationType);

            sb.Append($"<tr bgcolor=\"{rowBg}\" style=\"background:{rowBg};\">");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0;\"><span style=\"display:inline-block; background:{badgeBg}; color:{badgeFg}; font-size:11px; font-weight:600; padding:2px 8px; border-radius:999px; white-space:nowrap;\">{WebUtility.HtmlEncode(CategoryDisplayName(item.Category))}</span></td>");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0; color:#1e293b; font-weight:500;\">{WebUtility.HtmlEncode(item.Name)}</td>");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0; color:#64748b;\">{WebUtility.HtmlEncode(item.Label)}</td>");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0;\"><span style=\"display:inline-block; background:{expBadgeBg}; color:{expBadgeFg}; font-size:11px; font-weight:600; padding:2px 8px; border-radius:999px; white-space:nowrap;\">{WebUtility.HtmlEncode(ExpirationTypeDisplayName(expirationType))}</span></td>");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0; color:#64748b;\">{item.ExpiresAt:dd MMM yyyy}</td>");
            sb.Append($"<td align=\"right\" style=\"padding:8px; border-bottom:1px solid #e2e8f0; color:{accentColor}; font-weight:600; white-space:nowrap;\">{daysLabel}</td>");
            sb.Append("</tr>");
            rowIndex++;
        }

        sb.Append("</table></td></tr>");
    }

    private static void AppendHeaderCell(StringBuilder sb, string text, string align)
    {
        sb.Append($"<th align=\"{align}\" style=\"padding:8px; border-bottom:2px solid #e2e8f0; color:#64748b; font-size:11px; text-transform:uppercase; letter-spacing:0.03em;\">{WebUtility.HtmlEncode(text)}</th>");
    }

    // Category values here match ExpirationCheckService.Add() calls verbatim
    // ("PhysicalDevice", "Server", "License", "Circuit").
    private static (string Bg, string Fg) CategoryColors(string category) => category switch
    {
        "PhysicalDevice" => ("#dbeafe", "#2563eb"),
        "Server" => ("#ede9fe", "#7c3aed"),
        "License" => ("#d1fae5", "#059669"),
        "Circuit" => ("#e2e8f0", "#475569"),
        _ => ("#e2e8f0", "#334155")
    };

    private static string CategoryDisplayName(string category) => category switch
    {
        "PhysicalDevice" => "Physical Device",
        _ => category
    };

    // Matches Home/Index.cshtml's typeBadgeClass/typeLabels exactly (Tailwind's
    // amber-100/700, blue-100/700, red-100/700) so the same expiration type reads as the
    // same color whether you're looking at the dashboard or this email.
    private static (string Bg, string Fg) ExpirationTypeColors(string expirationType) => expirationType switch
    {
        "License" => ("#fef3c7", "#b45309"),
        "EndOfSupport" => ("#dbeafe", "#1d4ed8"),
        "EndOfLife" => ("#fee2e2", "#b91c1c"),
        _ => ("#e2e8f0", "#334155")
    };

    private static string ExpirationTypeDisplayName(string expirationType) => expirationType switch
    {
        "License" => "License",
        "EndOfSupport" => "End of Support",
        "EndOfLife" => "End of Life",
        _ => expirationType
    };
}
