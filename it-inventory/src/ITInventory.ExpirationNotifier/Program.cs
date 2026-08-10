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

builder.Services.AddScoped<IEmailNotificationService, DummyEmailNotificationService>();

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
