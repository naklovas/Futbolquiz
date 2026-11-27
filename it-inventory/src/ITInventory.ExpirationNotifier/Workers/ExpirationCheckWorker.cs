using ITInventory.ExpirationNotifier.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITInventory.ExpirationNotifier.Workers;

/// <summary>
/// Runs the expiration check once a day at a configured time (ExpirationNotifier:RunAtHour/
/// RunAtMinute), for as long as the Windows Service is running -- no Task Scheduler needed.
/// </summary>
public class ExpirationCheckWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpirationCheckWorker> _logger;
    private readonly TimeSpan _runAtTimeOfDay;

    public ExpirationCheckWorker(IServiceScopeFactory scopeFactory, ILogger<ExpirationCheckWorker> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var runAtHour = config.GetValue<int?>("ExpirationNotifier:RunAtHour") ?? 8;
        var runAtMinute = config.GetValue<int?>("ExpirationNotifier:RunAtMinute") ?? 0;
        _runAtTimeOfDay = new TimeSpan(runAtHour, runAtMinute, 0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Expiration Notifier service started. Daily run time: {RunAt}.", _runAtTimeOfDay);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun();
            _logger.LogInformation("Next check at {NextRun:yyyy-MM-dd HH:mm} (in {Delay}).", DateTime.Now + delay, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            await RunCheckAsync();
        }

        _logger.LogInformation("Expiration Notifier service stopping.");
    }

    private TimeSpan GetDelayUntilNextRun()
    {
        var now = DateTime.Now;
        var todayRun = now.Date + _runAtTimeOfDay;
        var nextRun = now < todayRun ? todayRun : todayRun.AddDays(1);
        return nextRun - now;
    }

    private async Task RunCheckAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<ExpirationCheckService>();
        try
        {
            await checker.RunAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Expiration check failed.");
        }
    }
}
