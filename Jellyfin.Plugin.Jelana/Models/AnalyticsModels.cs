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

public sealed record RankingItem(string Id, string Name, int Plays, long DurationSeconds, int UniqueViewers = 0);
public sealed record NameCount(string Name, int Count);
public sealed record DailyActivity(DateOnly Date, int Plays, long DurationSeconds);
public sealed record PlaybackSummary(int Plays, long DurationSeconds);
public sealed record PersonalPeriod(int Movies, int Episodes, long DurationSeconds);
public sealed record PersonalFavorite(string Id, string Name, int Plays, long DurationSeconds);
public sealed record PersonalGenre(string Name, long DurationSeconds);
public sealed record PersonalInsights(
    int ActiveDays,
    int UniqueTitles,
    long AverageWatchDaySeconds,
    int LongestStreakDays,
    PersonalFavorite? MostWatchedMovie,
    PersonalFavorite? MostWatchedSeries,
    PersonalGenre? MostWatchedGenre);
public sealed record ViewingHabits(
    string FavoriteWeekday,
    string FavoriteTimeOfDay,
    long LongestSessionSeconds,
    int MoviePercent,
    int EpisodePercent);
public sealed record PersonalAnalytics(
    PersonalPeriod Last30Days,
    PersonalPeriod LastYear,
    PersonalPeriod AllTime,
    ViewingHabits Habits30Days,
    ViewingHabits HabitsLastYear,
    ViewingHabits HabitsAllTime,
    PersonalInsights Insights30Days,
    PersonalInsights InsightsLastYear,
    PersonalInsights InsightsAllTime);
public sealed record MonthlyTrend(PlaybackSummary Current, PlaybackSummary Previous);
public sealed record TrendingItem(
    string Id,
    string Name,
    string Type,
    int CurrentPlays,
    int PreviousPlays,
    int UniqueViewers,
    int ActiveDays);
public sealed record LibraryCounts(int Movies, int Series, int Episodes, int Users);
public sealed record NewItemCounts(int Movies7, int Movies30, int Series7, int Series30);
public sealed record StorageBreakdown(IReadOnlyDictionary<string, long?> Libraries, long? Total);
public sealed record RecentItem(string Id, string Name, string Type, int? Year, DateTime DateCreated);
public sealed record MediaProfile(
    IReadOnlyDictionary<string, int> Video,
    IReadOnlyDictionary<string, int> Resolution,
    IReadOnlyDictionary<string, int> Audio);

public sealed record AnalyticsSnapshot(
    DateTimeOffset GeneratedAt,
    LibraryCounts Counts,
    StorageBreakdown Storage,
    NewItemCounts NewItems,
    PlaybackSummary Playback30,
    PlaybackSummary PlaybackAll,
    IReadOnlyList<RankingItem> TopMovies7,
    IReadOnlyList<RankingItem> TopMovies30,
    IReadOnlyList<RankingItem> TopMoviesAll,
    IReadOnlyList<RankingItem> TopSeries7,
    IReadOnlyList<RankingItem> TopSeries30,
    IReadOnlyList<RankingItem> TopSeriesAll,
    IReadOnlyList<RankingItem> TopUsers7,
    IReadOnlyList<RankingItem> TopUsers30,
    IReadOnlyList<RankingItem> TopUsersAll,
    IReadOnlyList<NameCount> PlaybackMethods,
    IReadOnlyList<NameCount> Clients,
    IReadOnlyList<DailyActivity> Activity,
    MonthlyTrend MonthlyTrend,
    IReadOnlyList<TrendingItem> Trending,
    IReadOnlyList<RecentItem> Recent,
    MediaProfile MediaProfile);
