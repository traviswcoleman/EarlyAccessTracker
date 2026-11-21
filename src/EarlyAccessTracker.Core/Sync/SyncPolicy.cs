using EarlyAccessTracker.Core.Data;

namespace EarlyAccessTracker.Core.Sync;

public sealed record SyncDecision(bool ShouldRun, string Reason);

/// <summary>
/// Decides whether a sync may hit Steam. Scheduled syncs run at most once per local calendar day;
/// a fully failed run may be retried later the same day, up to a hard per-day attempt cap.
/// </summary>
public static class SyncPolicy
{
    public static SyncDecision EvaluateScheduled(
        IEnumerable<SyncRun> recentRuns, DateTimeOffset now, TimeZoneInfo localZone, SyncOptions options)
    {
        var today = TimeZoneInfo.ConvertTime(now, localZone).Date;
        var todaysRuns = recentRuns
            .Where(r => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.StartedAt, DateTimeKind.Utc), localZone).Date == today)
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        if (todaysRuns.FirstOrDefault(r => r.Outcome is SyncOutcome.Succeeded or SyncOutcome.Partial) is { } done)
            return new(false, $"Already synced today ({done.Outcome}).");

        if (todaysRuns.Count >= options.MaxAttemptsPerDay)
            return new(false, $"Reached {options.MaxAttemptsPerDay} attempts today; will try again tomorrow.");

        if (todaysRuns.FirstOrDefault() is { Outcome: SyncOutcome.Failed } lastFailed)
        {
            var retryAt = (lastFailed.FinishedAt ?? lastFailed.StartedAt).AddMinutes(options.FailedRunRetryMinutes);
            if (now.UtcDateTime < retryAt)
                return new(false, $"Last attempt failed; next retry after {retryAt.ToLocalTime():t}.");
        }

        return new(true, todaysRuns.Count == 0 ? "No sync yet today." : "Retrying after a failed attempt.");
    }

    public static SyncDecision EvaluateManual(SyncRun? lastRun, DateTimeOffset now, SyncOptions options)
    {
        if (lastRun is { Outcome: SyncOutcome.Succeeded or SyncOutcome.Partial, FinishedAt: { } finished })
        {
            var allowedAt = finished.AddMinutes(options.ManualSyncMinIntervalMinutes);
            if (now.UtcDateTime < allowedAt)
                return new(false, $"Last sync finished at {finished.ToLocalTime():t}; manual sync available after {allowedAt.ToLocalTime():t}.");
        }
        return new(true, "OK");
    }
}
