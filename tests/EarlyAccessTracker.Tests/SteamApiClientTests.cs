using System.Net;
using EarlyAccessTracker.Core;
using EarlyAccessTracker.Core.Steam;
using Microsoft.Extensions.Logging.Abstractions;

namespace EarlyAccessTracker.Tests;

public class SteamApiClientTests
{
    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            var next = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
            return Task.FromResult(next(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static (SteamApiClient Client, ScriptedHandler Handler) Create(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var handler = new ScriptedHandler(responses);
        var client = new SteamApiClient(
            new HttpClient(handler),
            new StaticOptionsMonitor<SteamOptions>(new SteamOptions { ApiKey = "SECRET-KEY", SteamId = "76561190000000000" }),
            new StaticOptionsMonitor<SyncOptions>(new SyncOptions { MaxRetries = 2, RetryBaseDelayMs = 0 }),
            TimeProvider.System,
            NullLogger<SteamApiClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task StoreBrowse_UsesGetWithEncodedInputJson()
    {
        var (client, handler) = Create(_ => Json("""{"response":{"store_items":[]}}"""));
        await client.GetStoreItemsAsync([2823890], CancellationToken.None);

        var uri = Assert.Single(handler.Requests);
        Assert.StartsWith("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=", uri.AbsoluteUri);
        var json = Uri.UnescapeDataString(uri.Query["?input_json=".Length..]);
        Assert.Contains("\"appid\":2823890", json);
        Assert.Contains("\"include_release\":true", json);
    }

    [Fact]
    public async Task TransientServerError_IsRetried()
    {
        var (client, handler) = Create(
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            _ => Json("""{"response":{"store_items":[{"id":1,"success":1,"release":{}}]}}"""));

        var result = await client.GetStoreItemsAsync([1], CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
        Assert.False(Assert.Single(result).IsEarlyAccess);
    }

    [Fact]
    public async Task PersistentThrottling_ThrowsThrottled()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var ex = await Assert.ThrowsAsync<SteamApiException>(() => client.GetStoreItemsAsync([1], CancellationToken.None));
        Assert.True(ex.Throttled);
        Assert.Equal(3, handler.Requests.Count); // 1 + MaxRetries
    }

    [Fact]
    public async Task Unauthorized_FailsFast_WithoutLeakingKey()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<SteamApiException>(() => client.GetOwnedGamesAsync(CancellationToken.None));
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("SECRET-KEY", ex.Message);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task PrivateProfile_GivesActionableError()
    {
        var (client, _) = Create(_ => Json("""{"response":{}}"""));
        var ex = await Assert.ThrowsAsync<SteamApiException>(() => client.GetOwnedGamesAsync(CancellationToken.None));
        Assert.Contains("Game details", ex.Message);
    }
}
