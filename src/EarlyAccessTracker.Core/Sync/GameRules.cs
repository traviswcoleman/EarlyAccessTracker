using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Steam;

namespace EarlyAccessTracker.Core.Sync;

/// <summary>Pure state-transition rules for a single game.</summary>
public static class GameRules
{
    /// <summary>
    /// Applies one Store Browse observation. Anything other than a definite answer leaves the last
    /// known Early Access state untouched. Returns true when this observation is the first time we
    /// have seen the game go from Early Access to released.
    /// </summary>
    public static bool ApplyObservation(Game game, StoreItemStatus status, DateTime nowUtc)
    {
        switch (status.Result)
        {
            case StoreItemResult.Unavailable:
                game.StoreUnavailable = true;
                return false;
            case StoreItemResult.Ok when status.IsEarlyAccess is { } isEa:
                var previous = game.EarlyAccessCurrent;
                game.StoreUnavailable = false;
                game.EarlyAccessCurrent = isEa;
                game.EarlyAccessCheckedAt = nowUtc;
                game.SteamReleaseDate = status.SteamReleaseDate ?? game.SteamReleaseDate;
                game.OriginalSteamReleaseDate = status.OriginalSteamReleaseDate ?? game.OriginalSteamReleaseDate;

                if (previous == true && !isEa && game.ReleasedFromEarlyAccessAt is null)
                {
                    game.ReleasedFromEarlyAccessAt = nowUtc;
                    return true;
                }
                return false;
            default:
                return false; // Missing / NoReleaseData: unknown, preserve last good state.
        }
    }

    /// <summary>
    /// A game is alert-worthy once it has left Early Access and you haven't touched it since:
    /// never played, or last played before the release was observed and longer ago than the threshold.
    /// Playing the released version at any point suppresses the alert.
    /// </summary>
    public static bool ShouldRaiseAlert(Game game, DateTime nowUtc, TimeSpan inactivityThreshold) =>
        game.Owned
        && game.AlertStatus == AlertStatus.None
        && game.ReleasedFromEarlyAccessAt is { } released
        && (game.LastPlayedAt is null
            || (game.LastPlayedAt < released && game.LastPlayedAt < nowUtc - inactivityThreshold));
}
