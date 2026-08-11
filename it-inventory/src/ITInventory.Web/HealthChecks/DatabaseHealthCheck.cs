using ITInventory.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ITInventory.Web.HealthChecks;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ITInventoryDbContext _dbContext;

    public DatabaseHealthCheck(ITInventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // SqlClient's default connection timeout (~15-30s) is far longer than a probe's
        // default timeoutSeconds (Kubernetes/OpenShift default: 1s) -- an unreachable or
        // slow DB would otherwise make every probe time out before this check even returns,
        // so the pod would never go Ready even after the DB recovers. Bound it to something
        // a probe can realistically wait for (deployment-web.yaml sets timeoutSeconds: 5).
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await _dbContext.Database.CanConnectAsync(linkedCts.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database.CanConnectAsync returned false.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Database connection timed out.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed.", ex);
        }
    }
}
