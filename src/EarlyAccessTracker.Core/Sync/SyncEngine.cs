using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EarlyAccessTracker.Core.Sync;

/// <summary>Performs one complete sync run. Not thread-safe; use <see cref="SyncCoordinator"/>.</summary>
public sealed class SyncEngine(
    TrackerDbContext db,
    ISteamApiClient steam,
    IOptionsMonitor<SyncOptions> syncOptions,
    TimeProvider time,
    ILogger<SyncEngine> logger)
{
    private const int MaxConsecutiveBatchFailures = 2;

    public async Task<SyncRun> RunAsync(SyncTrigger trigger, Action<string> progress, CancellationToken ct)
    {
        var options = syncOptions.CurrentValue;
        var errors = new List<string>();
        var run = new SyncRun { Trigger = trigger, StartedAt = Now(), Outcome = SyncOutcome.Running };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            // 1. Library + playtime.
            progress("Fetching owned games…");
            var owned = await steam.GetOwnedGamesAsync(ct);
            if (owned.Count == 0)
                throw new SteamApiException("Steam returned an empty library. Check the profile's 'Game details' privacy setting.");

            var games = await db.Games.ToDictionaryAsync(g => g.AppId, ct);
            var seenAt = Now();
            var ownedIds = new HashSet<int>();
            foreach (var o in owned)
            {
                ownedIds.Add(o.AppId);
                if (!games.TryGetValue(o.AppId, out var game))
                {
                    game = new Game { AppId = o.AppId, FirstSeenAt = seenAt };
                    games[o.AppId] = game;
                    db.Games.Add(game);
                }
                game.Name = o.Name;
                game.Owned = true;
                game.LastSeenAt = seenAt;
                game.PlaytimeMinutes = o.PlaytimeMinutes;
                game.LastPlayedAt = o.LastPlayedAt ?? game.LastPlayedAt;
            }
            foreach (var game in games.Values.Where(g => g.Owned && !ownedIds.Contains(g.AppId)))
                game.Owned = false;

            run.OwnedGameCount = owned.Count;
            await db.SaveChangesAsync(ct);

            // 2. Early Access status, sequential batches.
            var ids = owned.Select(o => o.AppId).Distinct().Where(id => GameRules.NeedsStatusCheck(games[id])).ToList();
            run.RequestedCount = ids.Count;
            var batches = ids.Chunk(Math.Max(1, options.BatchSize)).ToList();
            var consecutiveFailures = 0;
            var abortReason = (string?)null;

            for (var i = 0; i < batches.Count; i++)
            {
                var batch = batches[i];
                if (abortReason is not null)
                {
                    run.FailedCount += batch.Length;
                    continue;
                }

                progress($"Checking Early Access status: batch {i + 1} of {batches.Count}…");
                if (i > 0 && options.DelayBetweenBatchesMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(options.DelayBetweenBatchesMs), time, ct);

                IReadOnlyList<StoreItemStatus> statuses;
                try
                {
                    statuses = await steam.GetStoreItemsAsync(batch, ct);
                    consecutiveFailures = 0;
                }
                catch (SteamApiException ex)
                {
                    run.FailedCount += batch.Length;
                    errors.Add($"Batch {i + 1}: {ex.Message}");
                    logger.LogWarning("Store Browse batch {Batch} failed: {Message}", i + 1, ex.Message);
                    if (ex.Throttled || ++consecutiveFailures >= MaxConsecutiveBatchFailures)
                    {
                        abortReason = ex.Throttled ? "Steam is throttling requests" : "repeated batch failures";
                        errors.Add($"Stopped scanning after batch {i + 1}: {abortReason}.");
                    }
                    continue;
                }

                var observedAt = Now();
                foreach (var status in statuses)
                {
                    switch (status.Result)
                    {
                        case StoreItemResult.Ok: run.SuccessfulCount++; break;
                        case StoreItemResult.Missing: run.MissingCount++; break;
                        default: run.UnavailableCount++; break;
                    }

                    if (!games.TryGetValue(status.AppId, out var game)) continue;
                    if (GameRules.ApplyObservation(game, status, observedAt))
                    {
                        run.NewTransitionCount++;
                        logger.LogInformation("{Name} ({AppId}) left Early Access", game.Name, game.AppId);
                    }

                    if (status.Result == StoreItemResult.Ok)
                    {
                        db.StatusHistory.Add(new StatusHistory
                        {
                            AppId = status.AppId,
                            ObservedAt = observedAt,
                            IsEarlyAccess = status.IsEarlyAccess,
                            Source = "StoreBrowse",
                            SyncRunId = run.Id,
                        });
                    }
                }
                await db.SaveChangesAsync(ct);
            }

            // 3. Alerts (covers new transitions and older ones that just crossed the inactivity threshold).
            var now = Now();
            var threshold = TimeSpan.FromDays(options.InactivityThresholdDays);
            foreach (var game in games.Values.Where(g => GameRules.ShouldRaiseAlert(g, now, threshold)))
            {
                game.AlertStatus = AlertStatus.Pending;
                game.AlertedAt = now;
                game.AlertStatusChangedAt = now;
                run.NewAlertCount++;
            }

            if (run.MissingCount > 0)
                errors.Add($"{run.MissingCount} requested app(s) were missing from Steam's responses.");

            run.Outcome = run.SuccessfulCount == 0 && run.RequestedCount > 0 ? SyncOutcome.Failed
                : run.FailedCount + run.MissingCount > 0 ? SyncOutcome.Partial
                : SyncOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Outcome = SyncOutcome.Failed;
            errors.Add("Sync was cancelled (app shutting down).");
        }
        catch (Exception ex)
        {
            run.Outcome = SyncOutcome.Failed;
            errors.Add(ex is SteamApiException ? ex.Message : $"Unexpected error: {ex.Message}");
            logger.LogError(ex, "Sync run failed");
        }
        finally
        {
            run.FinishedAt = Now();
            run.ErrorSummary = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return run;
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
