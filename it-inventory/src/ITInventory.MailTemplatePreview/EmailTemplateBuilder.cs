using System.Net;
using System.Text;

namespace ITInventory.MailTemplatePreview;

/// <summary>
/// Table-based layout with every style inline -- no &lt;style&gt; block, no flexbox/grid.
/// Outlook desktop (Word rendering engine) and a lot of corporate webmail strip or ignore
/// both, so this is the only layout approach that reliably looks the same everywhere.
/// </summary>
public static class EmailTemplateBuilder
{
    private const string FontFamily = "'Segoe UI', Arial, sans-serif";

    public static string BuildHtml(string countryName, int windowDays, IReadOnlyList<MockItem> expired, IReadOnlyList<MockItem> upcoming)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;

        sb.Append("<!DOCTYPE html><html><body style=\"margin:0; padding:0; background:#f1f5f9; font-family:").Append(FontFamily).Append(";\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f1f5f9; padding:24px 0;\"><tr><td align=\"center\">");
        sb.Append("<table role=\"presentation\" width=\"640\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#ffffff; border-radius:8px; overflow:hidden;\">");

        // Header
        sb.Append("<tr><td style=\"background:#0f172a; padding:24px 32px;\">");
        sb.Append("<div style=\"color:#ffffff; font-size:20px; font-weight:600;\">IT Inventory System</div>");
        sb.Append($"<div style=\"color:#94a3b8; font-size:13px; margin-top:4px;\">Expiration Notice &mdash; {WebUtility.HtmlEncode(countryName)}</div>");
        sb.Append("</td></tr>");

        // Summary + badges
        sb.Append("<tr><td style=\"padding:24px 32px 8px 32px;\">");
        sb.Append($"<p style=\"margin:0; color:#334155; font-size:14px; line-height:1.6;\">This is an automated summary of IT assets that have expired or are expiring within the next {windowDays} days.</p>");
        sb.Append("<div style=\"margin-top:16px;\">");
        sb.Append($"<span style=\"display:inline-block; background:#fee2e2; color:#dc2626; font-size:13px; font-weight:600; padding:4px 12px; border-radius:999px; margin-right:8px;\">{expired.Count} Expired</span>");
        sb.Append($"<span style=\"display:inline-block; background:#fef3c7; color:#d97706; font-size:13px; font-weight:600; padding:4px 12px; border-radius:999px;\">{upcoming.Count} Upcoming</span>");
        sb.Append("</div></td></tr>");

        if (expired.Count > 0)
        {
            AppendSection(sb, "Expired", "#dc2626", expired, now, isExpired: true);
        }

        if (upcoming.Count > 0)
        {
            AppendSection(sb, "Upcoming", "#d97706", upcoming, now, isExpired: false);
        }

        // Footer
        sb.Append("<tr><td style=\"padding:20px 32px; background:#f8fafc; border-top:1px solid #e2e8f0;\">");
        sb.Append("<p style=\"margin:0; color:#94a3b8; font-size:12px;\">This is an automated message from IT Inventory System. Please do not reply to this email.</p>");
        sb.Append($"<p style=\"margin:4px 0 0 0; color:#94a3b8; font-size:12px;\">Generated on {now:dd MMM yyyy HH:mm}</p>");
        sb.Append("</td></tr>");

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, string accentColor, IReadOnlyList<MockItem> items, DateTime now, bool isExpired)
    {
        sb.Append("<tr><td style=\"padding:16px 32px 8px 32px;\">");
        sb.Append($"<div style=\"color:{accentColor}; font-size:14px; font-weight:600; margin-bottom:8px;\">{WebUtility.HtmlEncode(title)} ({items.Count})</div>");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse; font-size:13px;\">");

        sb.Append("<tr>");
        AppendHeaderCell(sb, "Category", "left");
        AppendHeaderCell(sb, "Name", "left");
        AppendHeaderCell(sb, "Type", "left");
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

            sb.Append($"<tr style=\"background:{rowBg};\">");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0;\"><span style=\"display:inline-block; background:{badgeBg}; color:{badgeFg}; font-size:11px; font-weight:600; padding:2px 8px; border-radius:999px; white-space:nowrap;\">{WebUtility.HtmlEncode(CategoryDisplayName(item.Category))}</span></td>");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0; color:#1e293b; font-weight:500;\">{WebUtility.HtmlEncode(item.Name)}</td>");
            sb.Append($"<td style=\"padding:8px; border-bottom:1px solid #e2e8f0; color:#64748b;\">{WebUtility.HtmlEncode(item.Label)}</td>");
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

    // Category values here match ITInventory.ExpirationNotifier's ExpirationCheckService.Add()
    // calls verbatim ("PhysicalDevice", "Server", "License", "Circuit") on purpose, so this
    // whole file works unchanged once ported over to the real project.
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
}
