using System.Net;
using System.Net.Mail;
using ITInventory.MailTemplatePreview;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var host = config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not set.");
var port = config.GetValue<int?>("Smtp:Port") ?? 25;
var enableSsl = config.GetValue<bool?>("Smtp:EnableSsl") ?? false;
var username = config["Smtp:Username"] ?? string.Empty;
var password = config["Smtp:Password"] ?? string.Empty;
var fromAddress = config["Smtp:FromAddress"] ?? throw new InvalidOperationException("Smtp:FromAddress is not set.");
var fromDisplayName = config["Smtp:FromDisplayName"] ?? "IT Inventory System";
var toAddress = config["Smtp:ToAddress"] ?? throw new InvalidOperationException("Smtp:ToAddress is not set.");

// Sample data only -- no DB involved. Dates are relative to "now" so the email always looks
// sensible whenever you run this, no matter how much time has passed since you last tested.
var now = DateTime.Now;
var expired = new List<MockItem>
{
    new("PhysicalDevice", "Physical Device", "EndOfSupport", "FW-ISTANBUL-01", now.AddDays(-58)),
    new("Server", "Server", "EndOfLife", "SRV-DB-PROD-02", now.AddDays(-42)),
    new("License", "License (Expiration)", "License", "Microsoft 365 E3", now.AddDays(-23)),
};
var upcoming = new List<MockItem>
{
    new("Server", "Server", "EndOfSupport", "SRV-APP-WEB-01", now.AddDays(29)),
    new("License", "License (Support)", "License", "Symantec Endpoint Protection", now.AddDays(44)),
    new("Circuit", "Circuit", "EndOfSupport", "MPLS-Ankara-Link", now.AddDays(54)),
    new("PhysicalDevice", "Physical Device", "EndOfLife", "SW-CORE-3", now.AddDays(81)),
};

const int windowDays = 90;
const string countryName = "Turkiye (Sample)";

// Drop the bank logo next to this project as logo.png or logo.jpg (see the .csproj) and it's
// picked up automatically -- no code changes needed. Without it, the header just falls back
// to text-only, same as before.
var logoPath = new[] { "logo.png", "logo.jpg" }
    .Select(name => Path.Combine(AppContext.BaseDirectory, name))
    .FirstOrDefault(File.Exists);
const string logoCid = "logo";

var html = EmailTemplateBuilder.BuildHtml(countryName, windowDays, expired, upcoming, logoPath != null ? logoCid : null);

// Also dump the rendered HTML to disk so you can eyeball it in a browser without waiting on
// mail delivery/spam filters every time you tweak EmailTemplateBuilder.cs. The cid: image
// reference won't resolve when opened directly like this (it's only valid inside the actual
// MIME message) -- that's expected, the browser preview is for layout/text, not the logo.
var previewPath = Path.Combine(AppContext.BaseDirectory, "preview.html");
File.WriteAllText(previewPath, html);
Console.WriteLine($"Wrote HTML preview to: {previewPath}");
Console.WriteLine(logoPath != null ? $"Logo found: {logoPath}" : "No logo.png/logo.jpg found next to the .exe -- sending without a logo.");

using var client = new SmtpClient(host, port) { EnableSsl = enableSsl };
if (!string.IsNullOrWhiteSpace(username))
{
    client.Credentials = new NetworkCredential(username, password);
}

using var message = new MailMessage
{
    From = new MailAddress(fromAddress, fromDisplayName),
    Subject = $"[IT Inventory] {countryName} - Expired/Upcoming Records ({expired.Count} expired, {upcoming.Count} upcoming)"
};
message.To.Add(toAddress);

// A logo needs an HTML body wrapped in an AlternateView with a LinkedResource attached to
// it (MIME multipart/related) so the <img src="cid:logo"> tag in the HTML has something to
// resolve against -- setting message.Body directly (like before) has no way to carry that
// linked attachment.
if (logoPath != null)
{
    var htmlView = AlternateView.CreateAlternateViewFromString(html, null, "text/html");
    var logo = new LinkedResource(logoPath) { ContentId = logoCid };
    htmlView.LinkedResources.Add(logo);
    message.AlternateViews.Add(htmlView);
}
else
{
    message.IsBodyHtml = true;
    message.Body = html;
}

Console.WriteLine($"Sending to {toAddress} via {host}:{port} (SSL={enableSsl})...");
await client.SendMailAsync(message);
Console.WriteLine("Sent.");
