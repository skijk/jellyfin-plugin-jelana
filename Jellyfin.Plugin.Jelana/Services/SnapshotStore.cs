using System.Text.Json;
using Jellyfin.Plugin.Jelana.Models;

namespace Jellyfin.Plugin.Jelana.Services;

public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly PlaybackStore _events;
    private string SnapshotPath => Path.Combine(Plugin.Instance.DataFolderPath, "snapshot.json");

    public SnapshotStore(PlaybackStore events) => _events = events;

    public async Task<AnalyticsSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SnapshotPath)) return null;
        await using var stream = File.OpenRead(SnapshotPath);
        return await JsonSerializer.DeserializeAsync<AnalyticsSnapshot>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AnalyticsSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = await _events.ReadAllAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var recent = rows.Where(x => x.StartedAt >= now.AddDays(-30)).ToList();
            static IReadOnlyList<RankingItem> Rank(IEnumerable<PlaybackRecord> source, Func<PlaybackRecord, string> id, Func<PlaybackRecord, string> name) =>
                source.GroupBy(x => new { Id = id(x), Name = name(x) })
                    .Select(x => new RankingItem(x.Key.Id, x.Key.Name, x.Count(), x.Sum(y => (long)y.DurationSeconds)))
                    .OrderByDescending(x => x.Plays).ThenByDescending(x => x.DurationSeconds).Take(10).ToList();
            static IReadOnlyList<NameCount> Counts(IEnumerable<PlaybackRecord> source, Func<PlaybackRecord, string> key) =>
                source.GroupBy(key).Select(x => new NameCount(x.Key, x.Count()))
                    .OrderByDescending(x => x.Count).Take(10).ToList();

            var snapshot = new AnalyticsSnapshot(
                now,
                rows.Count,
                rows.Sum(x => (long)x.DurationSeconds),
                recent.Count,
                recent.Sum(x => (long)x.DurationSeconds),
                Rank(recent.Where(x => x.ItemType == "Movie"), x => x.ItemId, x => x.ItemName),
                Rank(recent.Where(x => x.ItemType == "Episode"), x => x.ItemId, x => x.ItemName),
                Rank(recent, x => x.UserId, x => x.UserName),
                Counts(recent, x => x.PlaybackMethod),
                Counts(recent, x => x.Client),
                recent.GroupBy(x => DateOnly.FromDateTime(x.StartedAt.UtcDateTime))
                    .Select(x => new DailyActivity(x.Key, x.Count(), x.Sum(y => (long)y.DurationSeconds)))
                    .OrderBy(x => x.Date).ToList());

            Directory.CreateDirectory(Plugin.Instance.DataFolderPath);
            var temporary = SnapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, SnapshotPath, true);
            return snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
