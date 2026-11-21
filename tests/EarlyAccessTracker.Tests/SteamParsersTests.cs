using System.Text.Json;
using EarlyAccessTracker.Core.Steam;

namespace EarlyAccessTracker.Tests;

public class SteamParsersTests
{
    // Trimmed from a live IStoreBrowseService/GetItems response captured 2026-11-21.
    private const string LiveStoreResponse = """
    {"response":{"store_items":[
      {"item_type":0,"id":2823890,"success":1,"visible":true,"name":"Center Station Simulator","appid":2823890,"type":0,"is_early_access":true,
       "release":{"steam_release_date":1712307636,"is_early_access":true}},
      {"item_type":0,"id":1145360,"success":1,"visible":true,"name":"Hades","appid":1145360,"type":0,
       "release":{"steam_release_date":1600353507,"original_steam_release_date":1575997560}},
      {"item_type":0,"id":620,"success":1,"visible":true,"name":"Portal 2","appid":620,"type":0,
       "release":{"steam_release_date":1303186800}},
      {"item_type":0,"id":999999999,"success":15,"visible":false,"name":"","store_url_path":"app/0/","appid":0}
    ]}}
    """;

    [Fact]
    public void StoreItems_ReadsEarlyAccessFlag_AndTreatsOmittedFlagAsFalse()
    {
        var result = SteamParsers.ParseStoreItems(LiveStoreResponse, [2823890, 1145360, 620]).ToDictionary(s => s.AppId);

        Assert.Equal(StoreItemResult.Ok, result[2823890].Result);
        Assert.True(result[2823890].IsEarlyAccess);
        Assert.False(result[1145360].IsEarlyAccess);
        Assert.False(result[620].IsEarlyAccess);
        Assert.Equal(new DateTime(2019, 12, 10, 17, 6, 0, DateTimeKind.Utc), result[1145360].OriginalSteamReleaseDate);
        Assert.Null(result[620].OriginalSteamReleaseDate);
    }

    [Fact]
    public void StoreItems_UnavailableItemMatchedById_NotAppId()
    {
        var result = SteamParsers.ParseStoreItems(LiveStoreResponse, [999999999]);
        Assert.Equal(StoreItemResult.Unavailable, Assert.Single(result).Result);
        Assert.Null(result[0].IsEarlyAccess);
    }

    [Fact]
    public void StoreItems_RequestedButNotReturned_IsMissing()
    {
        var result = SteamParsers.ParseStoreItems(LiveStoreResponse, [620, 12345]).ToDictionary(s => s.AppId);
        Assert.Equal(StoreItemResult.Missing, result[12345].Result);
        Assert.Null(result[12345].IsEarlyAccess);
    }

    [Fact]
    public void StoreItems_SuccessWithoutReleaseObject_IsUnknown()
    {
        const string json = """{"response":{"store_items":[{"id":5,"success":1}]}}""";
        var item = Assert.Single(SteamParsers.ParseStoreItems(json, [5]));
        Assert.Equal(StoreItemResult.NoReleaseData, item.Result);
        Assert.Null(item.IsEarlyAccess);
    }

    [Fact]
    public void StoreItems_EmptyResponseObject_AllMissing()
    {
        var result = SteamParsers.ParseStoreItems("""{"response":{}}""", [1, 2]);
        Assert.All(result, r => Assert.Equal(StoreItemResult.Missing, r.Result));
    }

    [Fact]
    public void StoreItems_MalformedPayload_Throws()
    {
        Assert.Throws<FormatException>(() => SteamParsers.ParseStoreItems("""{"oops":1}""", [1]));
        Assert.ThrowsAny<JsonException>(() => SteamParsers.ParseStoreItems("<html>", [1]));
    }

    [Fact]
    public void OwnedGames_ParsesFields_AndZeroLastPlayedIsNull()
    {
        const string json = """
        {"response":{"game_count":2,"games":[
          {"appid":620,"name":"Portal 2","playtime_forever":754,"rtime_last_played":1700000000},
          {"appid":1145360,"name":"Hades","playtime_forever":0,"rtime_last_played":0}
        ]}}
        """;
        var games = SteamParsers.ParseOwnedGames(json)!;
        Assert.Equal(2, games.Count);
        Assert.Equal(754, games[0].PlaytimeMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, games[0].LastPlayedAt);
        Assert.Null(games[1].LastPlayedAt);
    }

    [Fact]
    public void OwnedGames_PrivateProfile_ReturnsNull()
    {
        Assert.Null(SteamParsers.ParseOwnedGames("""{"response":{}}"""));
    }
}
