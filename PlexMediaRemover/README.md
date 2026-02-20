# Plex Media Remover

Plex Media Remover is a simple console application designed to run locally alongside your Plex Media Server. It automatically cleans up your media libraries by removing movies and TV shows based on watch time and history. 

To prevent media from being automatically re-downloaded, it also integrates with Radarr and Sonarr to remove the entries and files from those services as well.

## Features

- **Automated Cleanup**: Removes media based on configurable time thresholds.
  - Delete media added over `X` months ago that has never been watched.
  - Delete media that was watched, but hasn't been viewed in the last `Y` months.
- **Radarr & Sonarr Integration**: Ensures deleted media is also removed from your *arr apps so it doesn't get grabbed again.
- **Dry Run Mode**: Safely test your rules without actually deleting any files.
- **Space Savings Estimation**: Calculates and displays the total file size of the media being removed.

## Command Line Arguments

You can customize the execution using the following arguments:
- `-config <path>`: Specify a custom path for the configuration file (default: `config.json`).
- `-log <path>`: Specify a file to output the logs to, in addition to the console.
- `-u` or `-unwatched <months>`: Override the `DeleteUnwatchedMonths` config value.
- `-w` or `-watched <months>`: Override the `DeleteWatchedMonths` config value.
- `-force`: Disable the default dry run mode and actually delete the media.

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
  "Rules": {
    "DeleteUnwatchedMonths": 12,
    "DeleteWatchedMonths": 6
  }
}
```

*Note: The application runs in Dry Run mode by default. No files will be deleted unless you pass the `-force` argument.*

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
