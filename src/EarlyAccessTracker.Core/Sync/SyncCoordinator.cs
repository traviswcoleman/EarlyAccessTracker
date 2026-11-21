using EarlyAccessTracker.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EarlyAccessTracker.Core.Sync;

/// <summary>
/// Single entry point for scheduled and manual syncs. Guarantees only one run at a time and
/// applies <see cref="SyncPolicy"/> so Steam is never hit more than intended.
/// </summary>
public sealed class SyncCoordinator(
    IServiceScopeFactory scopes,
    IOptionsMonitor<SteamOptions> steamOptions,
    IOptionsMonitor<SyncOptions> syncOptions,
    TimeProvider time,
    ILogger<SyncCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRunning { get; private set; }
    public string? Progress { get; private set; }

    /// <summary>Raised (on a background thread) whenever running state, progress or data changes.</summary>
    public event Action? Changed;

    /// <summary>Raised (on a background thread) after every finished sync run, e.g. to show a toast.</summary>
    public event Action<SyncRun>? SyncFinished;

    public void NotifyDataChanged() => Changed?.Invoke();

    /// <summary>Marks runs left as "Running" by a crash/kill as failed so they count as attempts.</summary>
    public async Task RecoverOrphanedRunsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
        var orphans = await db.SyncRuns.Where(r => r.Outcome == SyncOutcome.Running).ToListAsync(ct);
        foreach (var run in orphans)
        {
            run.Outcome = SyncOutcome.Failed;
            run.FinishedAt ??= run.StartedAt;
            run.ErrorSummary = "The app exited before this sync finished.";
        }
        if (orphans.Count > 0) await db.SaveChangesAsync(ct);
    }

    private static readonly SyncDecision NotConfigured =
        new(false, "Add your Steam API key and SteamID to the secrets file first.");

    public async Task<SyncDecision> CheckScheduledAsync(CancellationToken ct)
    {
        // Don't burn a daily attempt (or touch Steam) until credentials exist.
        if (!steamOptions.CurrentValue.IsConfigured) return NotConfigured;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
        var since = time.GetUtcNow().UtcDateTime.AddDays(-2);
        var recent = await db.SyncRuns.Where(r => r.StartedAt >= since).ToListAsync(ct);
        return SyncPolicy.EvaluateScheduled(recent, time.GetUtcNow(), time.LocalTimeZone, syncOptions.CurrentValue);
    }

    public async Task<SyncDecision> CheckManualAsync(CancellationToken ct)
    {
        if (IsRunning) return new(false, "A sync is already running.");
        if (!steamOptions.CurrentValue.IsConfigured) return NotConfigured;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
        var last = await db.SyncRuns.OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
        return SyncPolicy.EvaluateManual(last, time.GetUtcNow(), syncOptions.CurrentValue);
    }

    /// <summary>Runs a scheduled sync only if the once-per-day policy allows it.</summary>
    public async Task<SyncRun?> RunScheduledIfDueAsync(CancellationToken ct)
    {
        var decision = await CheckScheduledAsync(ct);
        if (!decision.ShouldRun)
        {
            logger.LogDebug("Scheduled sync skipped: {Reason}", decision.Reason);
            return null;
        }
        return await RunAsync(SyncTrigger.Scheduled, ct);
    }

    /// <summary>Runs a manual sync if the manual rate limit allows it; returns the refusal reason otherwise.</summary>
    public async Task<(SyncRun? Run, string? Refusal)> RunManualAsync(CancellationToken ct)
    {
        var decision = await CheckManualAsync(ct);
        if (!decision.ShouldRun) return (null, decision.Reason);
        var run = await RunAsync(SyncTrigger.Manual, ct);
        return run is null ? (null, "A sync is already running.") : (run, null);
    }

    private async Task<SyncRun?> RunAsync(SyncTrigger trigger, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return null;
        try
        {
            IsRunning = true;
            Changed?.Invoke();

            SyncRun run;
            using (var scope = scopes.CreateScope())
            {
                var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();
                run = await engine.RunAsync(trigger, p => { Progress = p; Changed?.Invoke(); }, ct);
            }

            logger.LogInformation("Sync {Id} finished: {Outcome}, {Alerts} new alert(s)", run.Id, run.Outcome, run.NewAlertCount);
            try { SyncFinished?.Invoke(run); }
            catch (Exception ex) { logger.LogWarning(ex, "SyncFinished handler failed"); }
            return run;
        }
        finally
        {
            IsRunning = false;
            Progress = null;
            _gate.Release();
            Changed?.Invoke();
        }
    }
}
