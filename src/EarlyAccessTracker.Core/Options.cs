namespace EarlyAccessTracker.Core;

/// <summary>Steam credentials. Bound from the "Steam" config section (user secrets in practice).</summary>
public sealed class SteamOptions
{
    public const string Section = "Steam";

    /// <summary>Steam Web API key. Never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>64-bit SteamID of the library owner (e.g. 7656119xxxxxxxxxx).</summary>
    public string? SteamId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SteamId);
}

public sealed class SyncOptions
{
    public const string Section = "Sync";

    /// <summary>App IDs per Store Browse request. Steam publishes no limit; 50 is a conservative start.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Pause between sequential Store Browse requests.</summary>
    public int DelayBetweenBatchesMs { get; set; } = 2000;

    /// <summary>Retries per request for transient failures (network, 5xx, 429).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for exponential backoff; actual delay is base * 2^attempt + jitter.</summary>
    public int RetryBaseDelayMs { get; set; } = 2000;

    /// <summary>A released game is alert-worthy when it hasn't been played for this many days.</summary>
    public int InactivityThresholdDays { get; set; } = 30;

    /// <summary>After a failed scheduled sync, wait this long before trying again the same day.</summary>
    public int FailedRunRetryMinutes { get; set; } = 120;

    /// <summary>Hard cap on scheduled sync attempts per calendar day (successful or not).</summary>
    public int MaxAttemptsPerDay { get; set; } = 3;

    /// <summary>"Sync now" is refused if a non-failed sync finished within this many minutes.</summary>
    public int ManualSyncMinIntervalMinutes { get; set; } = 60;

    /// <summary>Wait after app start before the first scheduled check (lets the network come up after logon).</summary>
    public int StartupDelaySeconds { get; set; } = 60;

    /// <summary>How often the running app re-checks whether a daily sync is due.</summary>
    public int CheckIntervalMinutes { get; set; } = 30;

    public string CountryCode { get; set; } = "US";
    public string Language { get; set; } = "english";
}

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EarlyAccessTracker");

    public static string DatabasePath => Path.Combine(DataDirectory, "tracker.db");

    public static string ErrorLogPath => Path.Combine(DataDirectory, "error.log");
}
