using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EarlyAccessTracker.Core.Steam;

public interface ISteamApiClient
{
    Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(CancellationToken ct);
    Task<IReadOnlyList<StoreItemStatus>> GetStoreItemsAsync(IReadOnlyCollection<int> appIds, CancellationToken ct);
}

/// <summary>
/// Thin Steam Web API client. Requests are sequential and retried with exponential backoff + jitter.
/// URLs (which contain the API key) are never logged or put in exception messages.
/// </summary>
public sealed class SteamApiClient(
    HttpClient http,
    IOptionsMonitor<SteamOptions> steamOptions,
    IOptionsMonitor<SyncOptions> syncOptions,
    TimeProvider time,
    ILogger<SteamApiClient> logger) : ISteamApiClient
{
    private const string OwnedGamesUrl = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/";
    private const string StoreBrowseUrl = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/";

    public async Task<IReadOnlyList<OwnedGame>> GetOwnedGamesAsync(CancellationToken ct)
    {
        var steam = steamOptions.CurrentValue;
        if (!steam.IsConfigured)
            throw new SteamApiException("Steam API key and/or SteamID are not configured.");

        var url = $"{OwnedGamesUrl}?key={Uri.EscapeDataString(steam.ApiKey!)}"
                  + $"&steamid={Uri.EscapeDataString(steam.SteamId!.Trim())}"
                  + "&include_appinfo=1&include_played_free_games=1&format=json";

        var body = await GetWithRetryAsync(url, "GetOwnedGames", ct);
        IReadOnlyList<OwnedGame>? games;
        try
        {
            games = SteamParsers.ParseOwnedGames(body);
        }
        catch (JsonException ex)
        {
            throw new SteamApiException("GetOwnedGames returned malformed JSON.", inner: ex);
        }

        return games ?? throw new SteamApiException(
            "Steam returned no game list. Make sure the SteamID is correct and that your Steam profile's "
            + "'Game details' privacy setting is Public.");
    }

    public async Task<IReadOnlyList<StoreItemStatus>> GetStoreItemsAsync(IReadOnlyCollection<int> appIds, CancellationToken ct)
    {
        var sync = syncOptions.CurrentValue;
        var input = new
        {
            ids = appIds.Select(id => new { appid = id }),
            context = new { language = sync.Language, country_code = sync.CountryCode, steam_realm = 1 },
            data_request = new { include_release = true },
        };
        var url = $"{StoreBrowseUrl}?input_json={Uri.EscapeDataString(JsonSerializer.Serialize(input))}";
        logger.LogDebug("Store Browse request for {Count} ids, URL length {Length}", appIds.Count, url.Length);

        var body = await GetWithRetryAsync(url, "Store Browse", ct);
        try
        {
            return SteamParsers.ParseStoreItems(body, appIds);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new SteamApiException($"Store Browse returned a malformed payload: {ex.Message}", inner: ex);
        }
    }

    private async Task<string> GetWithRetryAsync(string url, string operation, CancellationToken ct)
    {
        var sync = syncOptions.CurrentValue;
        var maxRetries = Math.Max(0, sync.MaxRetries);

        for (var attempt = 0; ; attempt++)
        {
            string failure;
            bool throttled = false;
            TimeSpan? retryAfter = null;

            try
            {
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                var code = (int)response.StatusCode;
                failure = $"{operation} returned HTTP {code} ({response.StatusCode})";
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new SteamApiException($"{failure}. Check the Steam Web API key.");

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throttled = true;
                    retryAfter = response.Headers.RetryAfter?.Delta;
                }
                else if (code < 500)
                {
                    throw new SteamApiException(failure); // other 4xx: not transient
                }
            }
            catch (HttpRequestException ex)
            {
                failure = $"{operation} network error: {ex.Message}";
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = $"{operation} timed out";
            }

            if (attempt >= maxRetries)
                throw new SteamApiException($"{failure} (gave up after {attempt + 1} attempts)", throttled);

            var backoff = TimeSpan.FromMilliseconds(sync.RetryBaseDelayMs * Math.Pow(2, attempt))
                          + TimeSpan.FromMilliseconds(Random.Shared.Next(0, Math.Max(1, sync.RetryBaseDelayMs / 2)));
            if (retryAfter is { } ra && ra > backoff) backoff = ra;

            logger.LogWarning("{Failure}; retrying in {Delay:F1}s (attempt {Attempt}/{Max})",
                failure, backoff.TotalSeconds, attempt + 1, maxRetries);
            await Task.Delay(backoff, time, ct);
        }
    }
}
