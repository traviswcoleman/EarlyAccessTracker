using Microsoft.EntityFrameworkCore;
using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Sync;

namespace EarlyAccessTracker.Tests;

public class ToPlayTests
{
    private static readonly DateTime Release = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Game Observed(DateTime? lastPlayed) =>
        new() { AppId = 1, Owned = true, ReleasedFromEarlyAccessAt = Release, LastPlayedAt = lastPlayed };

    private static Game Possible(DateTime? lastPlayed) => new()
    {
        AppId = 2, Owned = true, EarlyAccessCurrent = false, LastPlayedAt = lastPlayed,
        SteamReleaseDate = Release, OriginalSteamReleaseDate = Release.AddYears(-2),
    };

    [Fact] public void ObservedRelease_NeverPlayed_IsToPlay() => Assert.True(GameRules.IsToPlay(Observed(null)));

    [Fact] public void PlayedBeforeRelease_IsToPlay() => Assert.True(GameRules.IsToPlay(Observed(Release.AddDays(-10))));

    [Fact] public void PlayedAfterRelease_IsNotToPlay() => Assert.False(GameRules.IsToPlay(Observed(Release.AddDays(1))));

    [Fact] public void PossibleRelease_NeverPlayed_IsToPlay() => Assert.True(GameRules.IsToPlay(Possible(null)));

    [Fact] public void PossibleRelease_PlayedAfter_IsNotToPlay() => Assert.False(GameRules.IsToPlay(Possible(Release.AddDays(5))));

    [Fact] public void NotKnownToHaveLeftEa_IsNotToPlay() =>
        Assert.False(GameRules.IsToPlay(new Game { AppId = 3, Owned = true, EarlyAccessCurrent = false }));

    [Fact]
    public void Ignored_IsNotToPlay()
    {
        var g = Observed(null);
        g.AlertStatus = AlertStatus.Dismissed;
        Assert.False(GameRules.IsToPlay(g));
    }
}

public class IgnoreUpdateTests
{
    // Mirrors the dashboard's checkbox update so we know EF can translate it.
    private static async Task<AlertStatus> Toggle(Game seed, bool ignored)
    {
        using var testDb = new TestDb();
        using (var db = testDb.Create()) { db.Games.Add(seed); await db.SaveChangesAsync(); }
        using var db2 = testDb.Create();
        await db2.Games.Where(g => g.AppId == seed.AppId).ExecuteUpdateAsync(s => s
            .SetProperty(g => g.AlertStatus, g => ignored ? AlertStatus.Dismissed : g.AlertedAt == null ? AlertStatus.None : AlertStatus.Acknowledged));
        return (await db2.Games.AsNoTracking().SingleAsync()).AlertStatus;
    }

    [Fact] public async Task Ignore_SetsDismissed() => Assert.Equal(AlertStatus.Dismissed, await Toggle(new Game { AppId = 1, Name = "a" }, true));

    [Fact] public async Task Unignore_NeverAlerted_SetsNone() =>
        Assert.Equal(AlertStatus.None, await Toggle(new Game { AppId = 1, Name = "a", AlertStatus = AlertStatus.Dismissed }, false));

    [Fact] public async Task Unignore_AlreadyAlerted_SetsAcknowledged() =>
        Assert.Equal(AlertStatus.Acknowledged, await Toggle(new Game { AppId = 1, Name = "a", AlertStatus = AlertStatus.Dismissed, AlertedAt = DateTime.UtcNow }, false));
}
