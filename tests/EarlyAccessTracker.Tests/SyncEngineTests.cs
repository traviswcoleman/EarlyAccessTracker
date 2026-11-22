using EarlyAccessTracker.Core;
using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Steam;
using EarlyAccessTracker.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EarlyAccessTracker.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeSteamClient _steam = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly SyncOptions _options = new() { BatchSize = 2, DelayBetweenBatchesMs = 0 };

    private async Task<SyncRun> SyncAsync()
    {
        await using var ctx = _db.Create();
        var engine = new SyncEngine(ctx, _steam, new StaticOptionsMonitor<SyncOptions>(_options), _time, NullLogger<SyncEngine>.Instance);
        return await engine.RunAsync(SyncTrigger.Scheduled, _ => { }, CancellationToken.None);
    }

    private Game Load(int appId)
    {
        using var ctx = _db.Create();
        return ctx.Games.AsNoTracking().Single(g => g.AppId == appId);
    }

    [Fact]
    public async Task InitialImport_ThenRelease_RaisesOneAlert_AndDoesNotRepeat()
    {
        _steam.Owned.Add(new OwnedGame(1, "Early Game", 30, null));
        _steam.Owned.Add(new OwnedGame(2, "Done Game", 600, _time.Now.UtcDateTime.AddDays(-2)));
        _steam.SetEa(1, true);
        _steam.SetEa(2, false);

        var first = await SyncAsync();
        Assert.Equal(SyncOutcome.Succeeded, first.Outcome);
        Assert.Equal(0, first.NewAlertCount);
        Assert.True(Load(1).EarlyAccessCurrent);

        _time.Now = _time.Now.AddDays(1);
        _steam.SetEa(1, false);
        var second = await SyncAsync();
        Assert.Equal(1, second.NewTransitionCount);
        Assert.Equal(1, second.NewAlertCount);
        Assert.Equal(AlertStatus.Pending, Load(1).AlertStatus);
        Assert.Equal(AlertStatus.None, Load(2).AlertStatus); // never was EA as far as we know

        _time.Now = _time.Now.AddDays(1);
        var third = await SyncAsync();
        Assert.Equal(0, third.NewAlertCount);
    }

    [Fact]
    public async Task ReleasedGames_AreNotRechecked_ButEaAndNewGamesAre()
    {
        _steam.Owned.Add(new OwnedGame(1, "Early", 0, null));
        _steam.Owned.Add(new OwnedGame(2, "Released", 0, null));
        _steam.SetEa(1, true);
        _steam.SetEa(2, false);
        await SyncAsync();

        _time.Now = _time.Now.AddDays(1);
        _steam.Owned.Add(new OwnedGame(3, "New", 0, null));
        _steam.SetEa(3, true);
        _steam.SetEa(2, true); // would flip back if it were asked; it must not be asked
        var run = await SyncAsync();

        Assert.Equal(2, run.RequestedCount);
        Assert.Equal(false, Load(2).EarlyAccessCurrent);
        Assert.Equal(true, Load(3).EarlyAccessCurrent);
    }

    [Fact]
    public async Task NothingToCheck_Succeeds_WithoutCallingSteam()
    {
        _steam.Owned.Add(new OwnedGame(1, "Released", 0, null));
        _steam.SetEa(1, false);
        await SyncAsync();

        _time.Now = _time.Now.AddDays(1);
        var calls = _steam.StoreCalls;
        var run = await SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, run.Outcome);
        Assert.Equal(0, run.RequestedCount);
        Assert.Equal(calls, _steam.StoreCalls);
    }

    [Fact]
    public async Task FailedBatch_IsPartial_AndKeepsPreviousState()
    {
        for (var i = 1; i <= 4; i++)
        {
            _steam.Owned.Add(new OwnedGame(i, $"Game {i}", 0, null));
            _steam.SetEa(i, true);
        }
        await SyncAsync();

        _time.Now = _time.Now.AddDays(1);
        for (var i = 1; i <= 4; i++) _steam.SetEa(i, false);
        _steam.FailBatch = ids => ids.Contains(3) ? new SteamApiException("HTTP 500") : null;

        var run = await SyncAsync();
        Assert.Equal(SyncOutcome.Partial, run.Outcome);
        Assert.Equal(2, run.SuccessfulCount);
        Assert.Equal(2, run.FailedCount);
        Assert.Contains("HTTP 500", run.ErrorSummary);
        Assert.False(Load(1).EarlyAccessCurrent);
        Assert.True(Load(3).EarlyAccessCurrent); // failure is NOT read as "released"
        Assert.Null(Load(3).ReleasedFromEarlyAccessAt);
    }

    [Fact]
    public async Task Throttling_StopsRemainingBatches()
    {
        for (var i = 1; i <= 6; i++)
        {
            _steam.Owned.Add(new OwnedGame(i, $"Game {i}", 0, null));
            _steam.SetEa(i, true);
        }
        _steam.FailBatch = ids => ids.Contains(3) ? new SteamApiException("HTTP 429", throttled: true) : null;

        var run = await SyncAsync();
        Assert.Equal(2, _steam.StoreCalls); // batch 3 never sent
        Assert.Equal(4, run.FailedCount);
        Assert.Equal(SyncOutcome.Partial, run.Outcome);
    }

    [Fact]
    public async Task MissingItems_AreUnknown_AndMakeRunPartial()
    {
        _steam.Owned.Add(new OwnedGame(1, "A", 0, null));
        _steam.Owned.Add(new OwnedGame(2, "B", 0, null));
        _steam.SetEa(1, true);

        var run = await SyncAsync();
        Assert.Equal(SyncOutcome.Partial, run.Outcome);
        Assert.Equal(1, run.MissingCount);
        Assert.Null(Load(2).EarlyAccessCurrent);
    }

    [Fact]
    public async Task UnavailableItems_DoNotMakeRunPartial()
    {
        _steam.Owned.Add(new OwnedGame(1, "A", 0, null));
        _steam.Owned.Add(new OwnedGame(2, "Delisted", 0, null));
        _steam.SetEa(1, true);
        _steam.Store[2] = new StoreItemStatus(2, StoreItemResult.Unavailable);

        var run = await SyncAsync();
        Assert.Equal(SyncOutcome.Succeeded, run.Outcome);
        Assert.True(Load(2).StoreUnavailable);
    }

    [Fact]
    public async Task EmptyLibrary_FailsRun()
    {
        var run = await SyncAsync();
        Assert.Equal(SyncOutcome.Failed, run.Outcome);
        Assert.Contains("empty library", run.ErrorSummary);
    }

    [Fact]
    public async Task ReleasedButPlayedRecently_AlertsOnlyAfterThreshold()
    {
        _steam.Owned.Add(new OwnedGame(1, "A", 100, _time.Now.UtcDateTime.AddDays(-5)));
        _steam.SetEa(1, true);
        await SyncAsync();

        _time.Now = _time.Now.AddDays(1);
        _steam.SetEa(1, false);
        Assert.Equal(0, (await SyncAsync()).NewAlertCount);

        _time.Now = _time.Now.AddDays(30);
        Assert.Equal(1, (await SyncAsync()).NewAlertCount);
    }

    public void Dispose() => _db.Dispose();
}
