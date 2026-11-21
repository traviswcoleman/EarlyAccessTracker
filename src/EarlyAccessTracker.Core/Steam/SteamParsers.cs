using System.Text.Json;

namespace EarlyAccessTracker.Core.Steam;

/// <summary>Pure JSON parsing, kept separate from HTTP so it can be unit-tested against captured payloads.</summary>
public static class SteamParsers
{
    /// <summary>
    /// Parses IPlayerService/GetOwnedGames. Returns null when the response has no "games" array,
    /// which is what Steam sends when the profile's game details are private.
    /// </summary>
    public static IReadOnlyList<OwnedGame>? ParseOwnedGames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("games", out var games)
            || games.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<OwnedGame>(games.GetArrayLength());
        foreach (var g in games.EnumerateArray())
        {
            if (!g.TryGetProperty("appid", out var idEl) || !idEl.TryGetInt32(out var appId)) continue;

            var name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var playtime = g.TryGetProperty("playtime_forever", out var p) && p.TryGetInt32(out var pt) ? pt : 0;
            DateTime? lastPlayed = null;
            if (g.TryGetProperty("rtime_last_played", out var lp) && lp.TryGetInt64(out var secs) && secs > 0)
                lastPlayed = DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;

            result.Add(new OwnedGame(appId, string.IsNullOrWhiteSpace(name) ? $"App {appId}" : name, playtime, lastPlayed));
        }
        return result;
    }

    /// <summary>
    /// Parses IStoreBrowseService/GetItems. Every requested ID gets exactly one status.
    /// Throws <see cref="JsonException"/> / <see cref="FormatException"/> for a malformed payload so the
    /// whole batch is treated as failed rather than as "not Early Access".
    /// </summary>
    /// <remarks>
    /// Verified against live responses (2026-11-21): Steam omits <c>is_early_access</c> entirely when it is
    /// false (protobuf default), so a present <c>release</c> object without the flag means "not EA".
    /// Unavailable items come back with <c>success != 1</c> and <c>appid: 0</c>, so match on <c>id</c>.
    /// </remarks>
    public static IReadOnlyList<StoreItemStatus> ParseStoreItems(string json, IReadOnlyCollection<int> requestedIds)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var response))
            throw new FormatException("Store Browse response is missing the 'response' object.");

        var byId = new Dictionary<int, StoreItemStatus>();
        if (response.TryGetProperty("store_items", out var items))
        {
            if (items.ValueKind != JsonValueKind.Array)
                throw new FormatException("Store Browse 'store_items' is not an array.");

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;

                var success = item.TryGetProperty("success", out var s) && s.TryGetInt32(out var sv) ? sv : 0;
                if (success != 1)
                {
                    byId[id] = new StoreItemStatus(id, StoreItemResult.Unavailable);
                    continue;
                }

                if (!item.TryGetProperty("release", out var release) || release.ValueKind != JsonValueKind.Object)
                {
                    byId[id] = new StoreItemStatus(id, StoreItemResult.NoReleaseData);
                    continue;
                }

                var isEa = release.TryGetProperty("is_early_access", out var ea) && ea.ValueKind == JsonValueKind.True;
                byId[id] = new StoreItemStatus(
                    id,
                    StoreItemResult.Ok,
                    isEa,
                    ReadUnixDate(release, "steam_release_date"),
                    ReadUnixDate(release, "original_steam_release_date"));
            }
        }

        return requestedIds
            .Select(id => byId.TryGetValue(id, out var st) ? st : new StoreItemStatus(id, StoreItemResult.Missing))
            .ToList();
    }

    private static DateTime? ReadUnixDate(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.TryGetInt64(out var secs) && secs > 0
            ? DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime
            : null;
}
