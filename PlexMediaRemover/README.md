# Plex Media Remover

Plex Media Remover is a simple console application designed to run locally alongside your Plex Media Server. It automatically cleans up your media libraries by removing movies and TV shows based on watch history.

To prevent media from being automatically re-downloaded, it also integrates with Radarr and Sonarr to remove the entries and files from those services as well.

## Features

- **Automated Cleanup**: Removes media based on configurable time thresholds.
  - Delete media added over `X` months ago that has never been watched.
  - Delete media that was watched, but hasn't been viewed in the last `Y` months.
- **Tautulli Integration**: Uses Tautulli to check global watch stats across all users on your server.
- **Radarr & Sonarr Integration**: Ensures deleted media is also removed from your *arr apps so it doesn't get grabbed again.
- **Library Selection**: Target specific libraries via command line, config, or an interactive numbered prompt.
- **Dry Run Mode**: Safely preview what would be deleted without touching any files.
- **Space Savings Estimation**: Calculates and displays the total file size of the media being removed.
- **Deletion Results**: When running with `-force`, shows a per-service breakdown of what was deleted and any errors.

## Command Line Arguments

| Argument | Description |
|---|---|
| `-help`, `--help` | Show help and exit. |
| `-config <path>` | Path to the configuration file (default: `config.json`). |
| `-log <path>` | Write log output to a file in addition to the console. |
| `-lib <name>` | Target a specific library by name (can be repeated). Overrides `TargetLibraries` in config. |
| `-lib` | (no value) List available Plex libraries and exit. |
| `-u`, `-unwatched <n>` | Override the `DeleteUnwatchedMonths` config value for this run. |
| `-w`, `-watched <n>` | Override the `DeleteWatchedMonths` config value for this run. |
| `-force` | Actually delete media. Without this flag the app runs in dry run mode. |

### Examples

```bash
# Show available libraries
PlexMediaRemover -lib

# Dry run across all libraries (interactive library prompt)
PlexMediaRemover

# Dry run on a specific library
PlexMediaRemover -lib "Movies"

# Delete from multiple specific libraries
PlexMediaRemover -lib "Movies" -lib "TV Shows" -force

# Override thresholds and delete
PlexMediaRemover -u 6 -w 3 -force

# Use a custom config and write a log file
PlexMediaRemover -config my.json -log run.log
```

## Configuration

On the first run, the application generates a `config.json` file in the working directory. Update it with your API tokens and URLs before running again.

```json
{
  "Plex": {
    "Url": "http://192.168.1.100:32400",
    "Token": "YOUR_PLEX_TOKEN"
  },
  "Radarr": {
    "Url": "http://192.168.1.100:7878",
    "Token": "YOUR_RADARR_TOKEN"
  },
  "Sonarr": {
    "Url": "http://192.168.1.100:8989",
    "Token": "YOUR_SONARR_TOKEN"
  },
  "Tautulli": {
    "Url": "http://192.168.1.100:8181",
    "ApiKey": "YOUR_TAUTULLI_API_KEY"
  },
  "Rules": {
    "DeleteUnwatchedMonths": 12,
    "DeleteWatchedMonths": 6
  },
  "TargetLibraries": ["Movies", "TV Shows"]
}
```

### Configuration Fields

| Field | Description |
|---|---|
| `Plex.Url` | Base URL of your Plex server. |
| `Plex.Token` | Your Plex authentication token. |
| `Radarr.Url` / `Radarr.Token` | Radarr URL and API key. |
| `Sonarr.Url` / `Sonarr.Token` | Sonarr URL and API key. |
| `Tautulli.Url` / `Tautulli.ApiKey` | Tautulli URL and API key. **Required.** |
| `Rules.DeleteUnwatchedMonths` | Delete media that has never been watched if it was added more than this many months ago. Must be a positive integer. |
| `Rules.DeleteWatchedMonths` | Delete media whose last watch was more than this many months ago. Must be a positive integer. |
| `TargetLibraries` | Optional list of library names to process. If empty or omitted, all movie and show libraries are candidates (an interactive prompt is shown when running in a terminal). |

### Config Validation

The app will exit with a clear error message if:
- Any URL is missing or not a valid `http`/`https` address.
- Tautulli is not configured (it is **required** for accurate watch history).
- Either rule threshold is zero or negative.

> **Note:** The app runs in **Dry Run mode** by default. Nothing is deleted unless you pass `-force`.

### TV Show Behaviour

For TV shows, Tautulli history is checked at the **show level** (all episodes). If *any* episode has been played, the show is considered watched and the `DeleteWatchedMonths` threshold applies. Only shows with zero plays across all episodes are treated as unwatched.

### Important Tautulli Setting

To ensure partial watches are tracked correctly, configure Tautulli to log play history immediately:

**Tautulli → Settings → History Logging → set Ignore Interval to `0`**

## Example Output

### Dry Run

```text
Running in DRY RUN mode. No media will be deleted. Use -force to actually delete.
Fetching Plex libraries...

Available libraries:
  [1] Movies (movie)
  [2] TV Shows (show)

Enter library numbers to process (comma-separated), or press Enter for all: 1

Processing library: Movies (movie)
[MATCH] The Matrix - Watched, but last viewed 8.5 months ago (Threshold: 6) (Size: 12.50 GB)
   -> [DRY RUN] Would delete 'The Matrix' from Radarr and Plex.
[MATCH] Inception - Unwatched and added 14.2 months ago (Threshold: 12) (Size: 15.20 GB)
   -> [DRY RUN] Would delete 'Inception' from Radarr and Plex.

--- Summary ---
Mode:              DRY RUN (no deletions)
Libraries:         Movies (movie)
Unwatched rule:    Delete if not watched within 12 month(s) of being added
Watched rule:      Delete if last watched more than 6 month(s) ago
Config file:       config.json

Library: Movies (movie)
  Total Items: 850
  Total Size:  4.20 TB
  To Delete:   2 items (27.70 GB)

Finished processing. Total estimated space saving: 27.70 GB
```

### Force Mode

When run with `-force`, a deletion results section is appended:

```text
--- Deletion Results ---
  Radarr:  2 deleted
  Sonarr:  0 deleted
  Plex:    2 deleted
  No errors.
```


## Configuration

On the first run, the application will generate a `config.json` file in the same directory. You will need to update this file with your specific API tokens and URLs:

```json
{
  "Plex": {
    "Url": "http://localhost:32400",
    "Token": "YOUR_PLEX_TOKEN"
  },
  "Radarr": {
    "Url": "http://localhost:7878",
    "Token": "YOUR_RADARR_TOKEN"
  },
  "Sonarr": {
    "Url": "http://localhost:8989",
    "Token": "YOUR_SONARR_TOKEN"
  },
  "Tautulli": {
    "Url": "http://localhost:8181",
    "ApiKey": "YOUR_TAUTULLI_API_KEY"
  },
  "Rules": {
    "DeleteUnwatchedMonths": 12,
    "DeleteWatchedMonths": 6
  }
}
```

*Note: The application runs in Dry Run mode by default. No files will be deleted unless you pass the `-force` argument.*

### Important Tautulli Setting
To ensure that even partial watches are tracked correctly by this script, you must configure Tautulli to log all play history immediately. 
In Tautulli, go to **Settings -> History Logging** and set the **Ignore Interval** to `0`.

## Example Output

When the application finishes processing your libraries, it provides a detailed summary of the items processed and the estimated space saved:

```text
Fetching Plex libraries...

Processing library: Movies (movie)
[MATCH] The Matrix - Watched, but last viewed 8.5 months ago (Threshold: 6) (Size: 12.50 GB)
   -> [DRY RUN] Would delete 'The Matrix' from Radarr and Plex.
[MATCH] Inception - Unwatched and added 14.2 months ago (Threshold: 12) (Size: 15.20 GB)
   -> [DRY RUN] Would delete 'Inception' from Radarr and Plex.

Processing library: TV Shows (show)
[MATCH] Breaking Bad - Watched, but last viewed 10.1 months ago (Threshold: 6) (Size: 45.30 GB)
   -> [DRY RUN] Would delete 'Breaking Bad' from Sonarr and Plex.

--- Summary ---
Library: Movies (movie)
  Total Items: 850
  Total Size:  4.20 TB
  To Delete:   2 items (27.70 GB)
Library: TV Shows (show)
  Total Items: 120
  Total Size:  1.80 TB
  To Delete:   1 items (45.30 GB)

Finished processing. Total estimated space saving: 73.00 GB
```
