using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

var configPath = "config.json";
bool force = false;
int? unwatchedOverride = null;
int? watchedOverride = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "-config" && i + 1 < args.Length) configPath = args[++i];
    else if (args[i] == "-log" && i + 1 < args.Length) Logger.LogPath = args[++i];
    else if ((args[i] == "-u" || args[i] == "-unwatched") && i + 1 < args.Length && int.TryParse(args[++i], out int u)) unwatchedOverride = u;
    else if ((args[i] == "-w" || args[i] == "-watched") && i + 1 < args.Length && int.TryParse(args[++i], out int w)) watchedOverride = w;
    else if (args[i] == "-force") force = true;
}

if (!File.Exists(configPath))
{
    var defaultConfig = new AppConfig(
        new PlexConfig("http://localhost:32400", "YOUR_PLEX_TOKEN"),
        new RadarrConfig("http://localhost:7878", "YOUR_RADARR_TOKEN"),
        new SonarrConfig("http://localhost:8989", "YOUR_SONARR_TOKEN"),
        new RemovalRules(12, 6)
    );
    var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(configPath, json);
    Logger.Log($"Created default config at {configPath}. Please update it with your API tokens and run again.");
    return;
}

var config = JsonSerializer.Deserialize<AppConfig>(await File.ReadAllTextAsync(configPath));
if (config == null)
{
    Logger.Log("Failed to load config.");
    return;
}

if (unwatchedOverride.HasValue || watchedOverride.HasValue)
{
    config = config with
    {
        Rules = new RemovalRules(
            unwatchedOverride ?? config.Rules.DeleteUnwatchedMonths,
            watchedOverride ?? config.Rules.DeleteWatchedMonths
        )
    };
    Logger.Log($"Overriding rules from command line: Unwatched={config.Rules.DeleteUnwatchedMonths}m, Watched={config.Rules.DeleteWatchedMonths}m");
}

if (force)
{
    Logger.Log("Force mode enabled. Media WILL be deleted.");
}
else
{
    Logger.Log("Running in DRY RUN mode. No media will be deleted. Use -force to actually delete.");
}

using var httpClient = new HttpClient();
var plexClient = new PlexClient(httpClient, config.Plex);
var radarrClient = new RadarrClient(httpClient, config.Radarr);
var sonarrClient = new SonarrClient(httpClient, config.Sonarr);

Logger.Log("Fetching Plex libraries...");
var libraries = await plexClient.GetLibrariesAsync();

if (libraries == null || libraries.Count == 0)
{
    Logger.Log("No libraries found or failed to connect to Plex.");
    return;
}

var now = DateTimeOffset.UtcNow;
long grandTotalBytesSaved = 0;
var librarySummaries = new List<LibrarySummary>();

foreach (var lib in libraries)
{
    // Only process movie and show libraries
    if (lib.Type != "movie" && lib.Type != "show") continue;

    Logger.Log($"\nProcessing library: {lib.Title} ({lib.Type})");
    var items = await plexClient.GetLibraryItemsAsync(lib.Key);

    int libTotalItems = items.Count;
    long libTotalBytes = 0;
    int libDeletedItems = 0;
    long libDeletedBytes = 0;

    foreach (var item in items)
    {
        long itemSizeBytes = 0;
        int aggregatedViewCount = item.ViewCount;
        long aggregatedLastViewedAt = item.LastViewedAt;
        long aggregatedViewOffset = item.ViewOffset;

        if (lib.Type == "movie")
        {
            if (item.Media != null)
            {
                foreach (var media in item.Media)
                {
                    if (media.Part != null)
                    {
                        foreach (var part in media.Part)
                        {
                            itemSizeBytes += part.Size;
                        }
                    }
                }
            }
        }
        else if (lib.Type == "show")
        {
            aggregatedViewCount = 0;
            aggregatedLastViewedAt = 0;
            aggregatedViewOffset = 0;

            var episodes = await plexClient.GetMetadataLeavesAsync(item.RatingKey);
            foreach (var ep in episodes)
            {
                aggregatedViewCount += ep.ViewCount;
                aggregatedViewOffset += ep.ViewOffset;
                if (ep.LastViewedAt > aggregatedLastViewedAt)
                {
                    aggregatedLastViewedAt = ep.LastViewedAt;
                }

                if (ep.Media != null)
                {
                    foreach (var media in ep.Media)
                    {
                        if (media.Part != null)
                        {
                            foreach (var part in media.Part)
                            {
                                itemSizeBytes += part.Size;
                            }
                        }
                    }
                }
            }

            // Fallback to show-level properties just in case
            if (aggregatedViewCount == 0 && item.ViewedLeafCount > 0) aggregatedViewCount = item.ViewedLeafCount;
            if (aggregatedLastViewedAt == 0 && item.LastViewedAt > 0) aggregatedLastViewedAt = item.LastViewedAt;
        }

        libTotalBytes += itemSizeBytes;

        bool shouldDelete = false;
        string reason = "";

        var addedAt = DateTimeOffset.FromUnixTimeSeconds(item.AddedAt);
        var lastViewedAt = aggregatedLastViewedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(aggregatedLastViewedAt) : (DateTimeOffset?)null;

        bool isUnwatched = aggregatedViewCount == 0 && aggregatedViewOffset == 0;

        if (isUnwatched)
        {
            var monthsSinceAdded = (now - addedAt).TotalDays / 30.44; // Approx days in a month
            if (monthsSinceAdded > config.Rules.DeleteUnwatchedMonths)
            {
                shouldDelete = true;
                reason = $"Unwatched and added {monthsSinceAdded:F1} months ago (Threshold: {config.Rules.DeleteUnwatchedMonths})";
            }
        }
        else if (lastViewedAt.HasValue)
        {
            var monthsSinceLastWatched = (now - lastViewedAt.Value).TotalDays / 30.44;
            if (monthsSinceLastWatched > config.Rules.DeleteWatchedMonths)
            {
                shouldDelete = true;
                reason = $"Watched, but last viewed {monthsSinceLastWatched:F1} months ago (Threshold: {config.Rules.DeleteWatchedMonths})";
            }
        }

        if (shouldDelete)
        {
            libDeletedItems++;
            libDeletedBytes += itemSizeBytes;
            grandTotalBytesSaved += itemSizeBytes;

            Logger.Log($"[MATCH] {item.Title} - {reason} (Size: {FormatBytes(itemSizeBytes)})");

            if (!force)
            {
                Logger.Log($"   -> [DRY RUN] Would delete '{item.Title}' from {(lib.Type == "movie" ? "Radarr" : "Sonarr")} and Plex.");
                continue;
            }

            try
            {
                // 1. Remove from Radarr/Sonarr
                if (lib.Type == "movie")
                {
                    await radarrClient.DeleteMovieAsync(item.Title);
                }
                else if (lib.Type == "show")
                {
                    await sonarrClient.DeleteSeriesAsync(item.Title);
                }

                // 2. Remove from Plex
                await plexClient.DeleteItemAsync(item.RatingKey);
                Logger.Log($"   -> Deleted '{item.Title}' from Plex.");
            }
            catch (Exception ex)
            {
                Logger.Log($"   -> [ERROR] Failed to delete '{item.Title}': {ex.Message}");
            }
        }
    }

    librarySummaries.Add(new LibrarySummary(lib.Title, lib.Type, libTotalItems, libTotalBytes, libDeletedItems, libDeletedBytes));
}

Logger.Log("\n--- Summary ---");
foreach (var summary in librarySummaries)
{
    Logger.Log($"Library: {summary.Title} ({summary.Type})");
    Logger.Log($"  Total Items: {summary.TotalItems}");
    Logger.Log($"  Total Size:  {FormatBytes(summary.TotalSizeBytes)}");
    Logger.Log($"  To Delete:   {summary.DeletedItems} items ({FormatBytes(summary.DeletedSizeBytes)})");
}

Logger.Log($"\nFinished processing. Total estimated space saving: {FormatBytes(grandTotalBytesSaved)}");

string FormatBytes(long bytes)
{
    string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
    int counter = 0;
    decimal number = (decimal)bytes;
    while (Math.Round(number / 1024) >= 1)
    {
        number /= 1024;
        counter++;
    }
    return string.Format("{0:n2} {1}", number, suffixes[counter]);
}

// --- Models ---
public record LibrarySummary(string Title, string Type, int TotalItems, long TotalSizeBytes, int DeletedItems, long DeletedSizeBytes);
public record AppConfig(PlexConfig Plex, RadarrConfig Radarr, SonarrConfig Sonarr, RemovalRules Rules);
public record PlexConfig(string Url, string Token);
public record RadarrConfig(string Url, string Token);
public record SonarrConfig(string Url, string Token);
public record RemovalRules(int DeleteUnwatchedMonths, int DeleteWatchedMonths);

// --- Clients ---
public class PlexClient
{
    private readonly HttpClient _http;
    private readonly PlexConfig _config;

    public PlexClient(HttpClient http, PlexConfig config)
    {
        _http = http;
        _config = config;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{_config.Url.TrimEnd('/')}{path}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("X-Plex-Token", _config.Token);
        return req;
    }

    public async Task<List<PlexDirectory>> GetLibrariesAsync()
    {
        if (string.IsNullOrEmpty(_config.Token) || _config.Token == "YOUR_PLEX_TOKEN") return new();
        var req = CreateRequest(HttpMethod.Get, "/library/sections");
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<PlexMediaContainerResponse>();
        return data?.MediaContainer?.Directory ?? new List<PlexDirectory>();
    }

    public async Task<List<PlexMetadata>> GetLibraryItemsAsync(string sectionId)
    {
        var req = CreateRequest(HttpMethod.Get, $"/library/sections/{sectionId}/all");
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<PlexMediaContainerResponse>();
        return data?.MediaContainer?.Metadata ?? new List<PlexMetadata>();
    }

    public async Task<List<PlexMetadata>> GetMetadataLeavesAsync(string ratingKey)
    {
        var req = CreateRequest(HttpMethod.Get, $"/library/metadata/{ratingKey}/allLeaves");
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return new List<PlexMetadata>();
        var data = await res.Content.ReadFromJsonAsync<PlexMediaContainerResponse>();
        return data?.MediaContainer?.Metadata ?? new List<PlexMetadata>();
    }

    public async Task DeleteItemAsync(string ratingKey)
    {
        var req = CreateRequest(HttpMethod.Delete, $"/library/metadata/{ratingKey}");
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }
}

public class RadarrClient
{
    private readonly HttpClient _http;
    private readonly RadarrConfig _config;

    public RadarrClient(HttpClient http, RadarrConfig config)
    {
        _http = http;
        _config = config;
    }

    public async Task DeleteMovieAsync(string title)
    {
        if (string.IsNullOrEmpty(_config.Token) || _config.Token == "YOUR_RADARR_TOKEN") return;

        var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.Url.TrimEnd('/')}/api/v3/movie");
        req.Headers.Add("X-Api-Key", _config.Token);
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return;

        var movies = await res.Content.ReadFromJsonAsync<List<RadarrMovie>>();
        var movie = movies?.FirstOrDefault(m => m.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

        if (movie != null)
        {
            var delReq = new HttpRequestMessage(HttpMethod.Delete, $"{_config.Url.TrimEnd('/')}/api/v3/movie/{movie.Id}?deleteFiles=true");
            delReq.Headers.Add("X-Api-Key", _config.Token);
            await _http.SendAsync(delReq);
            Logger.Log($"   -> Deleted '{title}' from Radarr.");
        }
    }
}

public class SonarrClient
{
    private readonly HttpClient _http;
    private readonly SonarrConfig _config;

    public SonarrClient(HttpClient http, SonarrConfig config)
    {
        _http = http;
        _config = config;
    }

    public async Task DeleteSeriesAsync(string title)
    {
        if (string.IsNullOrEmpty(_config.Token) || _config.Token == "YOUR_SONARR_TOKEN") return;

        var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.Url.TrimEnd('/')}/api/v3/series");
        req.Headers.Add("X-Api-Key", _config.Token);
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return;

        var series = await res.Content.ReadFromJsonAsync<List<SonarrSeries>>();
        var show = series?.FirstOrDefault(s => s.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

        if (show != null)
        {
            var delReq = new HttpRequestMessage(HttpMethod.Delete, $"{_config.Url.TrimEnd('/')}/api/v3/series/{show.Id}?deleteFiles=true");
            delReq.Headers.Add("X-Api-Key", _config.Token);
            await _http.SendAsync(delReq);
            Logger.Log($"   -> Deleted '{title}' from Sonarr.");
        }
    }
}

public class PlexMediaContainerResponse { public PlexMediaContainer? MediaContainer { get; set; } }
public class PlexMediaContainer { public List<PlexDirectory>? Directory { get; set; } public List<PlexMetadata>? Metadata { get; set; } }
public class PlexDirectory { public string Key { get; set; } = ""; public string Type { get; set; } = ""; public string Title { get; set; } = ""; }
public class PlexMetadata {
    public string RatingKey { get; set; } = "";
    public string Title { get; set; } = "";
    public long AddedAt { get; set; }
    public long LastViewedAt { get; set; }
    public int ViewCount { get; set; }
    public int ViewedLeafCount { get; set; }
    public long ViewOffset { get; set; }
    public List<PlexMedia>? Media { get; set; }
}
public class PlexMedia { public List<PlexPart>? Part { get; set; } }
public class PlexPart { public long Size { get; set; } }
public class RadarrMovie { public int Id { get; set; } public string Title { get; set; } = ""; }
public class SonarrSeries { public int Id { get; set; } public string Title { get; set; } = ""; }

public static class Logger
{
    public static string? LogPath { get; set; }
    public static void Log(string message)
    {
        Console.WriteLine(message);
        if (!string.IsNullOrEmpty(LogPath))
        {
            File.AppendAllText(LogPath, message + Environment.NewLine);
        }
    }
}
