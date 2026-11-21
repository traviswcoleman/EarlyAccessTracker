using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Steam;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EarlyAccessTracker.Tests;

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>In-memory SQLite database that lives as long as the fixture.</summary>
internal sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        using var db = Create();
        db.Database.Migrate();
    }

    public TrackerDbContext Create() =>
        new(new DbContextOptionsBuilder<TrackerDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}

/// <summary>Scriptable Steam client: owned games plus a per-app EA answer (null = omit from response).</summary>
internal sealed class FakeSteamClient : ISteamApiClient
{
    public List<OwnedGame> Owned { get; } = [];
    public Dictionary<int, StoreItemStatus> Store { get; } = [];
    public Func<IReadOnlyCollection<int>, Exception?> FailBatch { get; set; } = _ => null;
    public int StoreCalls { get; private set; }

    public Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OwnedGame>>(Owned.ToList());

    public Task<IReadOnlyList<StoreItemStatus>> GetStoreItemsAsync(IReadOnlyCollection<int> appIds, CancellationToken ct)
    {
        StoreCalls++;
        if (FailBatch(appIds) is { } ex) throw ex;
        return Task.FromResult<IReadOnlyList<StoreItemStatus>>(appIds
            .Select(id => Store.TryGetValue(id, out var s) ? s : new StoreItemStatus(id, StoreItemResult.Missing))
            .ToList());
    }

    public void SetEa(int appId, bool isEa) => Store[appId] = new StoreItemStatus(appId, StoreItemResult.Ok, isEa);
}
