namespace EarlyAccessTracker.Core.Steam;

public sealed record OwnedGame(int AppId, string Name, int PlaytimeMinutes, DateTime? LastPlayedAt);

public enum StoreItemResult
{
    /// <summary>Item returned with success=1 and a release object; IsEarlyAccess is known.</summary>
    Ok,
    /// <summary>Steam returned the item with success != 1 (delisted, invalid, not visible in region).</summary>
    Unavailable,
    /// <summary>Returned successfully but without a release object; state is unknown.</summary>
    NoReleaseData,
    /// <summary>Requested ID absent from the response.</summary>
    Missing,
}

public sealed record StoreItemStatus(
    int AppId,
    StoreItemResult Result,
    bool? IsEarlyAccess = null,
    DateTime? SteamReleaseDate = null,
    DateTime? OriginalSteamReleaseDate = null);

public sealed class SteamApiException(string message, bool throttled = false, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Steam answered 429 (or kept failing with 5xx) — callers should stop sending traffic.</summary>
    public bool Throttled { get; } = throttled;
}
