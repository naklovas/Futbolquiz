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

var html = EmailTemplateBuilder.BuildHtml(countryName, windowDays, expired, upcoming);

// Also dump the rendered HTML to disk so you can eyeball it in a browser without waiting on
// mail delivery/spam filters every time you tweak EmailTemplateBuilder.cs.
var previewPath = Path.Combine(AppContext.BaseDirectory, "preview.html");
File.WriteAllText(previewPath, html);
Console.WriteLine($"Wrote HTML preview to: {previewPath}");

using var client = new SmtpClient(host, port) { EnableSsl = enableSsl };
if (!string.IsNullOrWhiteSpace(username))
{
    client.Credentials = new NetworkCredential(username, password);
}

using var message = new MailMessage
{
    From = new MailAddress(fromAddress, fromDisplayName),
    Subject = $"[IT Inventory] {countryName} - Expired/Upcoming Records ({expired.Count} expired, {upcoming.Count} upcoming)",
    IsBodyHtml = true,
    Body = html
};
message.To.Add(toAddress);

Console.WriteLine($"Sending to {toAddress} via {host}:{port} (SSL={enableSsl})...");
await client.SendMailAsync(message);
Console.WriteLine("Sent.");
