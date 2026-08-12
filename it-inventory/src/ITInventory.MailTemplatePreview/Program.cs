using ITInventory.Data;
using ITInventory.ExpirationNotifier.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Runs the real expiration check against the real database, right now, once -- no Windows
// Service install, no waiting for ExpirationNotifier:RunAtHour/RunAtMinute. Reuses the exact
// same ExpirationCheckService/SmtpEmailNotificationService/DummyEmailNotificationService
// classes as the real service (via a ProjectReference), so a clean run here means the
// service will behave identically once it's actually installed -- this is purely a faster
// way to run the same logic while testing, not a separate reimplementation of it.

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .SetMinimumLevel(LogLevel.Information));

var connectionString = config.GetConnectionString("ITInventory");
if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("CHANGE_ME"))
{
    Console.WriteLine("ConnectionStrings:ITInventory is not set. Set it with:");
    Console.WriteLine("  dotnet user-secrets set \"ConnectionStrings:ITInventory\" \"<real connection string>\"");
    return;
}

var dbOptions = new DbContextOptionsBuilder<ITInventoryDbContext>()
    .UseSqlServer(connectionString)
    .Options;
using var db = new ITInventoryDbContext(dbOptions);

var smtpEnabled = config.GetValue<bool?>("Smtp:Enabled") ?? false;
var smtpHost = config["Smtp:Host"];
var sendingForReal = smtpEnabled && !string.IsNullOrWhiteSpace(smtpHost) && smtpHost != "CHANGE_ME";

IEmailNotificationService emailService = sendingForReal
    ? new SmtpEmailNotificationService(config, loggerFactory.CreateLogger<SmtpEmailNotificationService>())
    : new DummyEmailNotificationService(loggerFactory.CreateLogger<DummyEmailNotificationService>());

Console.WriteLine(sendingForReal
    ? "Smtp:Enabled=true -> this WILL send real emails to real recipients from the database."
    : "Smtp:Enabled=false -> DUMMY mode: nothing is actually sent, recipients/content are only logged below.");
Console.WriteLine();

var upcomingWindowDays = config.GetValue<int?>("ExpirationNotifier:UpcomingWindowDays") ?? 90;
var checker = new ExpirationCheckService(db, emailService, loggerFactory.CreateLogger<ExpirationCheckService>(), upcomingWindowDays);

await checker.RunAsync();

Console.WriteLine();
Console.WriteLine("Done.");
