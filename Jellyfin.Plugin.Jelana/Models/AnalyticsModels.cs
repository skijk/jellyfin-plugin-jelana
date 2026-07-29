namespace Jellyfin.Plugin.Jelana.Models;

public sealed record PlaybackRecord(
    DateTimeOffset StartedAt,
    int DurationSeconds,
    string UserId,
    string UserName,
    string ItemId,
    string ItemName,
    string ItemType,
    string Client,
    string Device,
    string PlaybackMethod);

public sealed record RankingItem(string Id, string Name, int Plays, long DurationSeconds);
public sealed record NameCount(string Name, int Count);
public sealed record DailyActivity(DateOnly Date, int Plays, long DurationSeconds);

public sealed record AnalyticsSnapshot(
    DateTimeOffset GeneratedAt,
    int TotalPlays,
    long TotalDurationSeconds,
    int Plays30Days,
    long Duration30DaysSeconds,
    IReadOnlyList<RankingItem> TopMovies,
    IReadOnlyList<RankingItem> TopSeries,
    IReadOnlyList<RankingItem> TopUsers,
    IReadOnlyList<NameCount> PlaybackMethods,
    IReadOnlyList<NameCount> Clients,
    IReadOnlyList<DailyActivity> Activity);
