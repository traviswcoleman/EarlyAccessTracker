using EarlyAccessTracker.Core;
using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Steam;
using EarlyAccessTracker.Core.Sync;

namespace EarlyAccessTracker.Tests;

public class GameRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromDays(30);

    private static StoreItemStatus Ok(bool ea) => new(1, StoreItemResult.Ok, ea);

    [Fact]
    public void TrueThenFalse_IsTransition_OnlyOnce()
    {
        var g = new Game { AppId = 1, EarlyAccessCurrent = true };
        Assert.True(GameRules.ApplyObservation(g, Ok(false), Now));
        Assert.Equal(Now, g.ReleasedFromEarlyAccessAt);
        Assert.False(GameRules.ApplyObservation(g, Ok(false), Now.AddDays(1)));
        Assert.Equal(Now, g.ReleasedFromEarlyAccessAt);
    }

    [Fact]
    public void FirstObservationFalse_IsNotTransition()
    {
        var g = new Game { AppId = 1 };
        Assert.False(GameRules.ApplyObservation(g, Ok(false), Now));
        Assert.Null(g.ReleasedFromEarlyAccessAt);
    }

    [Theory]
    [InlineData(StoreItemResult.Missing)]
    [InlineData(StoreItemResult.NoReleaseData)]
    [InlineData(StoreItemResult.Unavailable)]
    public void UnknownResults_PreserveLastKnownState(StoreItemResult result)
    {
        var g = new Game { AppId = 1, EarlyAccessCurrent = true, EarlyAccessCheckedAt = Now.AddDays(-1) };
        Assert.False(GameRules.ApplyObservation(g, new StoreItemStatus(1, result), Now));
        Assert.True(g.EarlyAccessCurrent);
        Assert.Equal(Now.AddDays(-1), g.EarlyAccessCheckedAt);
    }

    [Fact]
    public void EaThenUnknownThenReleased_StillTransitions()
    {
        var g = new Game { AppId = 1, EarlyAccessCurrent = true };
        GameRules.ApplyObservation(g, new StoreItemStatus(1, StoreItemResult.Missing), Now);
        Assert.True(GameRules.ApplyObservation(g, Ok(false), Now.AddDays(1)));
    }

    [Fact]
    public void Alert_NeverPlayed() =>
        Assert.True(GameRules.ShouldRaiseAlert(Released(lastPlayed: null), Now, Threshold));

    [Fact]
    public void Alert_LastPlayedLongAgo() =>
        Assert.True(GameRules.ShouldRaiseAlert(Released(lastPlayed: Now.AddDays(-90)), Now, Threshold));

    [Fact]
    public void NoAlert_PlayedRecentlyDuringEarlyAccess_UntilThresholdPasses()
    {
        var g = Released(lastPlayed: Now.AddDays(-10));
        Assert.False(GameRules.ShouldRaiseAlert(g, Now, Threshold));
        Assert.True(GameRules.ShouldRaiseAlert(g, Now.AddDays(25), Threshold));
    }

    [Fact]
    public void NoAlert_PlayedAfterRelease() =>
        Assert.False(GameRules.ShouldRaiseAlert(Released(lastPlayed: Now.AddDays(1)), Now.AddDays(60), Threshold));

    [Theory]
    [InlineData(AlertStatus.Pending)]
    [InlineData(AlertStatus.Acknowledged)]
    [InlineData(AlertStatus.Dismissed)]
    public void NoReAlert_OnceAlertStateExists(AlertStatus status)
    {
        var g = Released(lastPlayed: null);
        g.AlertStatus = status;
        Assert.False(GameRules.ShouldRaiseAlert(g, Now, Threshold));
    }

    private static Game Released(DateTime? lastPlayed) => new()
    {
        AppId = 1, Owned = true, EarlyAccessCurrent = false, ReleasedFromEarlyAccessAt = Now, LastPlayedAt = lastPlayed,
    };
}

public class SyncPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly SyncOptions Options = new() { FailedRunRetryMinutes = 120, MaxAttemptsPerDay = 3, ManualSyncMinIntervalMinutes = 60 };

    private static SyncRun Run(DateTimeOffset started, SyncOutcome outcome, int minutes = 5) => new()
    {
        StartedAt = started.UtcDateTime, FinishedAt = started.UtcDateTime.AddMinutes(minutes), Outcome = outcome,
    };

    private static SyncDecision Scheduled(params SyncRun[] runs) =>
        SyncPolicy.EvaluateScheduled(runs, Now, TimeZoneInfo.Utc, Options);

    [Fact] public void NoRuns_Runs() => Assert.True(Scheduled().ShouldRun);

    [Fact] public void RanYesterday_Runs() => Assert.True(Scheduled(Run(Now.AddDays(-1), SyncOutcome.Succeeded)).ShouldRun);

    [Theory]
    [InlineData(SyncOutcome.Succeeded)]
    [InlineData(SyncOutcome.Partial)]
    public void AlreadyRanToday_Skips(SyncOutcome outcome) =>
        Assert.False(Scheduled(Run(Now.AddHours(-14), outcome)).ShouldRun);

    [Fact]
    public void FailedRecently_WaitsForRetryInterval()
    {
        Assert.False(Scheduled(Run(Now.AddMinutes(-30), SyncOutcome.Failed)).ShouldRun);
        Assert.True(Scheduled(Run(Now.AddHours(-3), SyncOutcome.Failed)).ShouldRun);
    }

    [Fact]
    public void FailedTooManyTimesToday_Skips() =>
        Assert.False(Scheduled(
            Run(Now.AddHours(-9), SyncOutcome.Failed),
            Run(Now.AddHours(-6), SyncOutcome.Failed),
            Run(Now.AddHours(-3), SyncOutcome.Failed)).ShouldRun);

    [Fact]
    public void Manual_BlockedShortlyAfterGoodSync_AllowedAfterFailure()
    {
        Assert.False(SyncPolicy.EvaluateManual(Run(Now.AddMinutes(-20), SyncOutcome.Succeeded), Now, Options).ShouldRun);
        Assert.True(SyncPolicy.EvaluateManual(Run(Now.AddMinutes(-20), SyncOutcome.Failed), Now, Options).ShouldRun);
        Assert.True(SyncPolicy.EvaluateManual(Run(Now.AddHours(-2), SyncOutcome.Succeeded), Now, Options).ShouldRun);
    }
}
