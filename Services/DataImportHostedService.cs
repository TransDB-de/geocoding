namespace transdb_geocoding.Services;

/// <summary>
/// Hosted service that runs the GeoNames data import in the background
/// so the HTTP server (including /health) is reachable during startup.
/// Updates <see cref="ReadinessService"/> to reflect the current state.
/// </summary>
public class DataImportHostedService(
    IServiceScopeFactory scopeFactory,
    ReadinessService readiness,
    ILogger<DataImportHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield once so the HTTP server finishes starting before we begin the import.
        // This ensures /health is reachable from the very first Docker healthcheck probe.
        await Task.Yield();

        readiness.SetImporting();
        logger.LogInformation("Data import check started");

        try
        {
            using var scope = scopeFactory.CreateScope();
            
            var importService = scope.ServiceProvider.GetRequiredService<IDataImportService>();

            await importService.EnsureDataImportedAsync(stoppingToken);

            readiness.SetReady();
            logger.LogInformation("GeoNames data check complete - service is ready");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown during import — not a failure
            logger.LogWarning("Data import cancelled due to application shutdown");
        }
        catch (Exception ex)
        {
            readiness.SetFailed();
            logger.LogError(ex, "GeoNames import failed - service degraded, queries will return empty results");
        }
    }
}
