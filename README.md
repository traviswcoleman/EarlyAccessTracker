# Early Access Tracker

A Windows tray app that tracks your Steam library and shows a toast when games leave Early Access. See [docs/Spec.md](docs/Spec.md) for the full spec.

## Setup

1. **Secrets.** Put your Steam Web API key and 64-bit SteamID in
   `%APPDATA%\Microsoft\UserSecrets\EarlyAccessTracker-6f1c2a8e-3b4d-4e5f-9a7b-1c2d3e4f5a6b\secrets.json`:
   ```json
   { "Steam": { "ApiKey": "YOUR_KEY", "SteamId": "7656119XXXXXXXXXX" } }
   ```
   The file sits outside the repo. A running app reloads it when it changes. Your Steam profile's **Game details** setting must be **Public**.
2. **Install.** In Visual Studio, right-click *EarlyAccessTracker.App*, choose **Publish**, and pick the **LocalInstall** profile. Or run `.\scripts\Install-ScheduledTask.ps1 -StartNow`. Both do the same thing. This publishes the app to `%LOCALAPPDATA%\Programs\EarlyAccessTracker` and registers a scheduled task for your user that runs at logon and daily at 09:00.
3. **Use it.** Click the **EA** tray icon, or open <http://127.0.0.1:5179/>. Right-click the icon for *Sync now* and *Exit*.

## How syncing is limited

- A scheduled sync runs at most **once per local calendar day**. The app checks the `SyncRuns` table to decide, so restarts and repeat task launches don't trigger extra Steam requests.
- After a failed sync, it retries after 2 hours, with at most 3 attempts per day.
- *Sync now* is refused for 60 minutes after a successful sync.
- Store Browse requests run one at a time in batches of 50, with a 2-second gap, exponential backoff, and a stop on HTTP 429.
- No sync runs until the API key and SteamID are configured.

All limits can be changed in `appsettings.json` under `Sync`.

## Data

The SQLite database lives at `%LOCALAPPDATA%\EarlyAccessTracker\tracker.db`. Crash details are written to `error.log` in the same folder.

## Development

```powershell
dotnet build
dotnet test
dotnet run --project src/EarlyAccessTracker.App          # opens the dashboard
dotnet tool restore; dotnet ef migrations add <Name> -p src/EarlyAccessTracker.Core -s src/EarlyAccessTracker.Core -o Data/Migrations
```
