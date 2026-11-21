using EarlyAccessTracker.Core;
using EarlyAccessTracker.Core.Sync;
using Microsoft.Extensions.Options;

namespace EarlyAccessTracker.App;

/// <summary>
/// While the tray app is running, periodically asks the coordinator whether today's sync is due.
/// The once-per-day decision itself is made from the sync history in the database, so restarting
/// the app (or the scheduled task launching it again) never causes an extra Steam scan.
/// </summary>
public sealed class DailySyncScheduler(
    SyncCoordinator coordinator,
    IOptionsMonitor<SyncOptions> options,
    TimeProvider time,
    ILogger<DailySyncScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await coordinator.RecoverOrphanedRunsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, options.CurrentValue.StartupDelaySeconds)), time, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await coordinator.RunScheduledIfDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Scheduled sync check failed");
                    ErrorLog.Write("Scheduled sync check failed", ex);
                }

                var interval = TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.CheckIntervalMinutes));
                await Task.Delay(interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
