using System.Text.Json;
using Jellyfin.Plugin.Jelana.Models;

namespace Jellyfin.Plugin.Jelana.Services;

public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly PlaybackReportingReader _reporting;
    private readonly LibraryAnalyticsReader _library;
    private string SnapshotPath => Path.Combine(Plugin.Instance.DataFolderPath, "snapshot.json");
    private string PersonalPath => Path.Combine(Plugin.Instance.DataFolderPath, "personal-snapshot.json");

    public SnapshotStore(PlaybackReportingReader reporting, LibraryAnalyticsReader library)
    {
        _reporting = reporting;
        _library = library;
    }

    public async Task<AnalyticsSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SnapshotPath)) return null;
        await using var stream = File.OpenRead(SnapshotPath);
        return await JsonSerializer.DeserializeAsync<AnalyticsSnapshot>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PersonalAnalytics?> ReadPersonalAsync(string userId, CancellationToken cancellationToken)
    {
        if (!File.Exists(PersonalPath)) return null;
        await using var stream = File.OpenRead(PersonalPath);
        var values = await JsonSerializer.DeserializeAsync<Dictionary<string, PersonalAnalytics>>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        return values?.GetValueOrDefault(userId.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant())
            ?? new PersonalAnalytics(
                new PersonalPeriod(0, 0, 0),
                new PersonalPeriod(0, 0, 0),
                new PersonalPeriod(0, 0, 0),
                new ViewingHabits("–", "–", 0, 0, 0));
    }

    public async Task<AnalyticsSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var playback = await _reporting.ReadAsync(cancellationToken).ConfigureAwait(false);
            var library = await _library.ReadAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = new AnalyticsSnapshot(
                DateTimeOffset.Now,
                library.Counts,
                library.Storage,
                library.NewItems,
                playback.Playback30,
                playback.PlaybackAll,
                playback.TopMovies7,
                playback.TopMovies30,
                playback.TopSeries7,
                playback.TopSeries30,
                playback.TopUsers7,
                playback.TopUsers30,
                playback.PlaybackMethods,
                playback.Clients,
                playback.Activity,
                playback.MonthlyTrend,
                playback.Trending,
                library.Recent,
                library.MediaProfile);

            Directory.CreateDirectory(Plugin.Instance.DataFolderPath);
            var temporary = SnapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, SnapshotPath, true);
            var personalTemporary = PersonalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(
                personalTemporary,
                JsonSerializer.Serialize(playback.Personal, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            File.Move(personalTemporary, PersonalPath, true);
            return snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
