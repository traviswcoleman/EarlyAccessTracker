using System.ComponentModel.DataAnnotations.Schema;

namespace EarlyAccessTracker.Core.Data;

public enum AlertStatus { None = 0, Pending = 1, Acknowledged = 2, Dismissed = 3 }

public enum SyncOutcome { Running = 0, Succeeded = 1, Partial = 2, Failed = 3 }

public enum SyncTrigger { Scheduled = 0, Manual = 1 }

/// <summary>All timestamps are stored in UTC.</summary>
public class Game
{
    public int AppId { get; set; }
    public string Name { get; set; } = "";

    public bool Owned { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public int PlaytimeMinutes { get; set; }
    public DateTime? LastPlayedAt { get; set; }

    /// <summary>Last known Early Access state. Null = never successfully observed.</summary>
    public bool? EarlyAccessCurrent { get; set; }
    public DateTime? EarlyAccessCheckedAt { get; set; }

    /// <summary>Steam returned an explicit "not available" item (delisted, region-locked, etc.).</summary>
    public bool StoreUnavailable { get; set; }

    /// <summary>Store release date (for formerly-EA games this is the 1.0 date).</summary>
    public DateTime? SteamReleaseDate { get; set; }

    /// <summary>Only present on some apps; for formerly-EA games it is the original Early Access launch.</summary>
    public DateTime? OriginalSteamReleaseDate { get; set; }

    /// <summary>First time we observed is_early_access go true → false.</summary>
    public DateTime? ReleasedFromEarlyAccessAt { get; set; }

    public AlertStatus AlertStatus { get; set; }
    public DateTime? AlertedAt { get; set; }
    public DateTime? AlertStatusChangedAt { get; set; }

    /// <summary>
    /// Heuristic for games that were already out of EA when first imported: Steam kept an earlier
    /// "original" release date. Not proof (re-releases can carry one too), so it never raises alerts.
    /// </summary>
    [NotMapped]
    public bool PossiblyFormerlyEarlyAccess =>
        ReleasedFromEarlyAccessAt is null
        && EarlyAccessCurrent == false
        && OriginalSteamReleaseDate is { } original
        && SteamReleaseDate is { } release
        && original < release;

    [NotMapped]
    public string StoreUrl => $"https://store.steampowered.com/app/{AppId}/";
}

public class StatusHistory
{
    public long Id { get; set; }
    public int AppId { get; set; }
    public DateTime ObservedAt { get; set; }
    public bool? IsEarlyAccess { get; set; }
    public string Source { get; set; } = "";
    public int SyncRunId { get; set; }
}

public class SyncRun
{
    public int Id { get; set; }
    public SyncTrigger Trigger { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public SyncOutcome Outcome { get; set; }
    public string? ErrorSummary { get; set; }

    public int OwnedGameCount { get; set; }
    public int RequestedCount { get; set; }
    public int SuccessfulCount { get; set; }
    /// <summary>Items in batches whose HTTP request failed after retries (or were skipped after throttling).</summary>
    public int FailedCount { get; set; }
    /// <summary>Requested IDs absent from an otherwise successful response.</summary>
    public int MissingCount { get; set; }
    /// <summary>Steam explicitly reported the item unavailable, or returned it without release data.</summary>
    public int UnavailableCount { get; set; }

    public int NewTransitionCount { get; set; }
    public int NewAlertCount { get; set; }
}
