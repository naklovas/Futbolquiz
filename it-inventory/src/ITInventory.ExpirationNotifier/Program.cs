using ITInventory.Data;
using ITInventory.ExpirationNotifier.Logging;
using ITInventory.ExpirationNotifier.Services;
using ITInventory.ExpirationNotifier.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Lets the same .exe run as a normal console app (dotnet run, for testing) or as an
// installed Windows Service (sc.exe create ... binPath= "...exe") -- it detects which.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ITInventory Expiration Notifier";
});

var connectionString = builder.Configuration.GetConnectionString("ITInventory");
builder.Services.AddDbContext<ITInventoryDbContext>(options => options.UseSqlServer(connectionString));

// Relay not configured yet (or Smtp:Enabled is false) -> keep logging what would be sent
// instead of actually sending. Once the relay details are filled in and Enabled is set to
// true, this switches to real email with no other code changes needed.
var smtpEnabled = builder.Configuration.GetValue<bool?>("Smtp:Enabled") ?? false;
var smtpHost = builder.Configuration["Smtp:Host"];
var useRealSmtp = smtpEnabled && !string.IsNullOrWhiteSpace(smtpHost) && smtpHost != "CHANGE_ME";

// Printed unconditionally (not behind ILogger, which isn't built yet) so a wrong Smtp:Enabled/
// Smtp:Host reading is visible immediately at startup instead of silently falling back to the
// dummy sender -- appsettings.json path resolution issues (stale copy in bin\, wrong working
// directory, etc.) show up here as the values not matching what's actually in the file.
Console.WriteLine($"[Startup] Smtp:Enabled={smtpEnabled}, Smtp:Host=\"{smtpHost}\" -> {(useRealSmtp ? "SmtpEmailNotificationService (real mail will be sent)" : "DummyEmailNotificationService (log only, no mail sent)")}");
Console.WriteLine($"[Startup] appsettings.json read from: {Path.Combine(AppContext.BaseDirectory, "appsettings.json")}");

if (useRealSmtp)
{
    builder.Services.AddScoped<IEmailNotificationService, SmtpEmailNotificationService>();
}
else
{
    builder.Services.AddScoped<IEmailNotificationService, DummyEmailNotificationService>();
}

var upcomingWindowDays = builder.Configuration.GetValue<int?>("ExpirationNotifier:UpcomingWindowDays") ?? 90;
builder.Services.AddScoped(sp => new ExpirationCheckService(
    sp.GetRequiredService<ITInventoryDbContext>(),
    sp.GetRequiredService<IEmailNotificationService>(),
    sp.GetRequiredService<ILogger<ExpirationCheckService>>(),
    upcomingWindowDays));

builder.Services.AddHostedService<ExpirationCheckWorker>();

// A Windows Service has no console to watch -- write a plain daily text log too, next to
// whatever's built-in (console when run interactively, Windows Event Log when installed
// as a service, both wired up automatically by AddWindowsService above).
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
builder.Logging.AddProvider(new FileLoggerProvider(logDirectory));

var host = builder.Build();
await host.RunAsync();
