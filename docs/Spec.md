# Early Access Tracker — Product and Technical Spec

## Purpose

Track Steam games owned by one user, detect when a game leaves Steam Early Access, and alert when it has been fully released and has not been played recently. Provide a dashboard with the complete library and release status.

## User and deployment assumptions

- Initial scope is a single user's Steam library (approximately 1,200 games).
- Prefer a local Windows application first. A later Azure deployment is possible, but the local approach keeps the initial system simple and avoids hosting a service.
- Store data in a small local database such as SQLite.
- Run a daily scheduled sync and make the dashboard available locally.
- A configurable inactivity threshold defaults to 30 days.

## Data sources and findings

### Owned games and play history

Use Steam's `IPlayerService/GetOwnedGames` Web API method to import the library and playtime metadata. This requires a Steam Web API key and the user's game details must be visible to the API. Capture the app ID, name, total playtime, and the available last-played timestamp. Steam data availability/privacy limitations should be surfaced in setup and sync status.

### Early Access flag

Do not use `https://store.steampowered.com/api/appdetails` as the bulk status source. It is an undocumented Store endpoint, and its plural `appids` parameter does not reliably provide full app details for multiple IDs; multi-ID requests have been observed to work only with a restricted price filter. Its rate limits and reset window are undocumented.

Use the public Steam Store Browse endpoint instead:

`GET https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=<URL-encoded JSON>`

The request JSON takes an `ids` list, a context, and a `data_request`. Example for Center Station Simulator (AppID 2823890):

```json
{
  "ids": [{ "appid": 2823890 }],
  "context": {
    "language": "english",
    "country_code": "US",
    "steam_realm": 1
  },
  "data_request": {
    "include_release": true
  }
}
```

Read the Early Access state from each returned item's `release.is_early_access` property. The response shape is `response.store_items[]`.

This endpoint was manually verified in Postman: a single-app request for Center Station Simulator returned `is_early_access: true`, and a request containing multiple app IDs also succeeded.

The endpoint is separate from the local Steam `appcache/appinfo.vdf` cache. The local cache/console investigation did not establish a dependable Early Access field: `app_info_print <appid>` showed `ReleaseState: "released"`, which describes whether the app is released/playable and does not mean that it has left Early Access. Do not rely on parsing `appinfo.vdf` unless a later test proves the required flag is present and complete for the library.

### Request method and URL size

A Postman POST to Store Browse returned HTTP 405 in the manual test. Use GET with a URL-encoded `input_json` query parameter unless later testing establishes another supported method.

Steam does not publish a maximum number of IDs or a URL length limit for this route. A rough estimate for the small `include_release` payload is about 30–35 encoded URL characters per seven-digit ID, plus a small fixed request overhead. Fifty IDs should produce a URL around 1.6–2 KB; one hundred IDs around 3.2–3.7 KB. These are estimates, not guaranteed limits.

Start with configurable batches of 50 IDs. Validate each response and verify that all requested IDs were returned. Then test 100 and potentially 250 IDs in Postman or the implementation. Do not assume a Steam request-rate or batch cap from the `appdetails` endpoint applies to Store Browse; Store Browse's limits are undocumented. Use conservative sequential requests, bounded retries, and backoff on failures.

## Core workflow

1. Initial import: fetch owned games and persist their IDs, names, playtime, last-played values, and current Early Access state.
2. Daily scan: refresh owned-game/playtime data, then request Store Browse metadata in batches for tracked app IDs.
3. Persist each successful state observation with a timestamp. Treat missing/failed results as unknown, not as `false`.
4. Detect a transition from `is_early_access=true` to `false`. Record the first observed transition time and mark the game as a release candidate.
5. Alert when a release candidate has no recorded play in the last configurable threshold (default 30 days). If Steam provides no last-played date for a never-played game, treat it as not played recently.
6. Avoid duplicate alerts. Store alert state and allow acknowledgement/dismissal; do not re-alert daily for the same transition.
7. Show the full library and sync/alert details in a local dashboard.

## Suggested data model

### Game

- `app_id` (primary key)
- `name`
- `owned` / first-seen and last-seen timestamps
- `playtime_minutes`
- `last_played_at` (nullable)
- `early_access_current` (nullable boolean)
- `early_access_checked_at`
- `released_from_early_access_at` (nullable; first observed transition)
- `alert_status` (none, pending, sent, acknowledged, dismissed)
- `alerted_at` (nullable)

### StatusHistory (recommended)

- `app_id`
- `observed_at`
- `is_early_access` (nullable boolean)
- `source`
- `request_batch_id` or sync run ID

### SyncRun

- start/end timestamps
- outcome and error summary
- number of owned games
- requested, successful, failed, and missing item counts

## Dashboard requirements

- Full game list with search and sorting.
- Filters for currently Early Access, newly released/unplayed, played recently, and dismissed/acknowledged.
- Columns for game name, Early Access state, last played, playtime, release transition first observed, and alert state.
- Manual “sync now” action and last successful sync timestamp.
- Sync errors and partial responses visible to the user.
- Link each title to its Steam Store page.

## Notifications

For the first version, dashboard-visible pending alerts are sufficient. Desktop notifications are a straightforward local enhancement. Email can be added later if desired.

## Reliability and correctness requirements

- Store Browse is not documented with a stable rate limit or maximum batch size. Make batch size configurable and keep requests sequential initially.
- Never interpret an HTTP failure, missing item, malformed payload, or omitted `release` object as “not Early Access.” Preserve the last good state and report the scan as partial.
- Retry transient network/server errors with exponential backoff and jitter; stop/reduce traffic on repeated throttling or 5xx errors.
- Keep a sync log so failures are distinguishable from “no changes.”
- Keep the Steam Web API key out of source control and logs.
- Status is observed once per day, so alert timing can lag an actual 1.0 release by up to roughly one day plus retry delays.
- `is_early_access=false` after a previously true observation is the transition signal. A game that was already fully released at first import cannot be identified as formerly Early Access without historical data; optionally support manual backfill/override.

## Validation plan before implementation

1. Use Postman to test one known Early Access title, one formerly Early Access title, and one standard fully released title.
2. Confirm the returned `release.is_early_access` values match the Steam store pages.
3. Test batches of 50, 100, and 250 IDs; record URL length, HTTP status, response item count, and time.
4. Confirm whether inaccessible, delisted, DLC, tool, or unknown app IDs return an explicit unsuccessful item, are omitted, or fail the entire batch.
5. Test a deliberately invalid ID and malformed request handling before building the daily scanner.
6. Test `GetOwnedGames` privacy/API-key requirements for this account and confirm the last-played values available for owned, uninstalled games.

## Open decisions

- Local-only first version vs Azure deployment later.
- Desktop notification vs dashboard-only for the first release.
- Whether to include games first seen as already fully released via a manual “previously Early Access” flag.
- Final Store Browse batch size after the Postman tests.