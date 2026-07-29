using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Jelana.Models;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Jelana.Services;

/// <summary>
/// Ports Jelana's Jellyfin-library portion into the hourly snapshot job.
/// </summary>
public sealed class LibraryAnalyticsReader
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IMediaSourceManager _mediaSources;

    public LibraryAnalyticsReader(
        ILibraryManager library,
        IUserManager users,
        IMediaSourceManager mediaSources)
    {
        _library = library;
        _users = users;
        _mediaSources = mediaSources;
    }

    public Task<LibraryAnalytics> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private LibraryAnalytics Read(CancellationToken cancellationToken)
    {
        var items = _library.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode]
        });
        var movies = items.Where(x => x.GetBaseItemKind() == BaseItemKind.Movie).ToList();
        var series = items.Where(x => x.GetBaseItemKind() == BaseItemKind.Series).ToList();
        var episodes = items.Where(x => x.GetBaseItemKind() == BaseItemKind.Episode).ToList();
        var now = DateTime.UtcNow;
        var recentCandidates = movies.Concat(series).ToList();
        var newItems = new NewItemCounts(
            movies.Count(x => x.DateCreated >= now.AddDays(-7)),
            movies.Count(x => x.DateCreated >= now.AddDays(-30)),
            series.Count(x => x.DateCreated >= now.AddDays(-7)),
            series.Count(x => x.DateCreated >= now.AddDays(-30)));
        var recent = recentCandidates
            .OrderByDescending(x => x.DateCreated)
            .Take(8)
            .Select(x => new RecentItem(
                x.Id.ToString("N"),
                x.Name,
                x.GetBaseItemKind().ToString(),
                x.ProductionYear,
                x.DateCreated))
            .ToList();

        var profile = ReadOrBuildMediaProfile(movies.Concat(episodes), cancellationToken);

        return new LibraryAnalytics(
            new LibraryCounts(movies.Count, series.Count, episodes.Count, _users.GetUsers().Count()),
            ReadStorage(cancellationToken),
            newItems,
            recent,
            profile);
    }

    private MediaProfile ReadOrBuildMediaProfile(
        IEnumerable<BaseItem> items,
        CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(Plugin.Instance.DataFolderPath, "media-profile.json");
        if (File.Exists(cachePath)
            && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromHours(6))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<MediaProfile>(File.ReadAllText(cachePath));
                if (cached is not null) return cached;
            }
            catch (IOException) { }
            catch (JsonException) { }
        }

        var video = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resolution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var audio = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var streams = _mediaSources.GetMediaStreams(item.Id);
            var videoStream = streams.FirstOrDefault(x => x.Type == MediaStreamType.Video);
            var audioStream = streams.FirstOrDefault(x => x.Type == MediaStreamType.Audio);
            if (videoStream is not null)
            {
                Increment(video, (videoStream.Codec ?? "UNKNOWN").ToUpperInvariant());
                Increment(resolution, ClassifyResolution(videoStream.Width, videoStream.Height));
            }

            if (audioStream is not null)
            {
                Increment(audio, (audioStream.Codec ?? "UNKNOWN").ToUpperInvariant());
            }
        }

        var profile = new MediaProfile(Sorted(video), Sorted(resolution), Sorted(audio));
        Directory.CreateDirectory(Plugin.Instance.DataFolderPath);
        var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profile));
        File.Move(temporary, cachePath, true);
        return profile;
    }

    private StorageBreakdown ReadStorage(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, long?>();
        foreach (var folder in _library.GetVirtualFolders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long total = 0;
                foreach (var location in folder.Locations)
                {
                    total += Directory.EnumerateFiles(location, "*", SearchOption.AllDirectories)
                        .Sum(path =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try { return new FileInfo(path).Length; }
                            catch (IOException) { return 0L; }
                            catch (UnauthorizedAccessException) { return 0L; }
                        });
                }

                values[folder.Name] = total;
            }
            catch (IOException) { values[folder.Name] = null; }
            catch (UnauthorizedAccessException) { values[folder.Name] = null; }
        }

        return new StorageBreakdown(
            values,
            values.Values.All(x => x.HasValue) ? values.Values.Sum(x => x!.Value) : null);
    }

    private static string ClassifyResolution(int? width, int? height)
    {
        if (height >= 2000 || width >= 3800) return "4K";
        if (height >= 1000 || width >= 1900) return "1080p";
        if (height >= 700 || width >= 1200) return "720p";
        return "SD";
    }

    private static void Increment(IDictionary<string, int> values, string key)
    {
        values.TryGetValue(key, out var count);
        values[key] = count + 1;
    }

    private static IReadOnlyDictionary<string, int> Sorted(Dictionary<string, int> values) =>
        values.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
}

public sealed record LibraryAnalytics(
    LibraryCounts Counts,
    StorageBreakdown Storage,
    NewItemCounts NewItems,
    IReadOnlyList<RecentItem> Recent,
    MediaProfile MediaProfile);
