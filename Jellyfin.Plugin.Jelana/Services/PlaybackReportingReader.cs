using System.Globalization;
using Jellyfin.Plugin.Jelana.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Jelana.Services;

/// <summary>
/// Faithful port of Jelana's Playback Reporting queries. Called only by the
/// hourly snapshot builder, never by an HTTP request.
/// </summary>
public sealed class PlaybackReportingReader
{
    private const int SessionGapSeconds = 1800;
    private readonly string _databasePath;
    private readonly IUserManager _users;
    private readonly ILibraryManager _library;

    public PlaybackReportingReader(IApplicationPaths paths, IUserManager users, ILibraryManager library)
    {
        _databasePath = Path.Combine(paths.DataPath, "playback_reporting.db");
        _users = users;
        _library = library;
    }

    public async Task<PlaybackAnalytics> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException(
                "Playback Reporting database was not found. Install and start Playback Reporting first.",
                _databasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA query_only=ON; PRAGMA busy_timeout=5000;", cancellationToken)
            .ConfigureAwait(false);

        return new PlaybackAnalytics(
            await SummaryAsync(connection, 30, cancellationToken).ConfigureAwait(false),
            await SummaryAsync(connection, null, cancellationToken).ConfigureAwait(false),
            await TopMoviesAsync(connection, 7, cancellationToken).ConfigureAwait(false),
            await TopMoviesAsync(connection, 30, cancellationToken).ConfigureAwait(false),
            await TopMoviesAsync(connection, null, cancellationToken).ConfigureAwait(false),
            await TopSeriesAsync(connection, 7, cancellationToken).ConfigureAwait(false),
            await TopSeriesAsync(connection, 30, cancellationToken).ConfigureAwait(false),
            await TopSeriesAsync(connection, null, cancellationToken).ConfigureAwait(false),
            await TopUsersAsync(connection, 7, cancellationToken).ConfigureAwait(false),
            await TopUsersAsync(connection, 30, cancellationToken).ConfigureAwait(false),
            await TopUsersAsync(connection, null, cancellationToken).ConfigureAwait(false),
            await PlaybackMethodsAsync(connection, 30, cancellationToken).ConfigureAwait(false),
            await CountsAsync(connection, "COALESCE(NULLIF(ClientName, ''), NULLIF(DeviceName, ''), 'Unknown')", 30, 6, cancellationToken).ConfigureAwait(false),
            await ActivityAsync(connection, 30, cancellationToken).ConfigureAwait(false),
            new MonthlyTrend(
                await SummaryAsync(connection, 30, cancellationToken).ConfigureAwait(false),
                await SummaryRangeAsync(connection, 60, 30, cancellationToken).ConfigureAwait(false)),
            await TrendingAsync(connection, cancellationToken).ConfigureAwait(false),
            await PersonalAnalyticsAsync(connection, cancellationToken).ConfigureAwait(false));
    }

    private static string SessionCte(string where = "") => $$"""
        WITH ordered AS (
            SELECT DateCreated, UserId, ItemId, ItemType, ItemName,
                   COALESCE(PlayDuration, 0) AS PlayDuration,
                   LAG(DateCreated) OVER (
                       PARTITION BY UserId, ItemId ORDER BY DateCreated
                   ) AS PreviousDate
            FROM PlaybackActivity
            {{where}}
        ),
        sessions AS (
            SELECT *,
                   CASE WHEN PreviousDate IS NULL THEN 1
                        WHEN (julianday(DateCreated) - julianday(PreviousDate)) * 86400 > {{SessionGapSeconds}} THEN 1
                        ELSE 0 END AS NewPlay
            FROM ordered
        )
        """;

    private static string Since(int days) =>
        DateTime.Now.AddDays(-Math.Max(1, days)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static async Task<PlaybackSummary> SummaryAsync(SqliteConnection db, int? days, CancellationToken token)
    {
        await using var command = db.CreateCommand();
        command.CommandText = SessionCte(days.HasValue ? "WHERE DateCreated >= $since" : "") +
            " SELECT COALESCE(SUM(NewPlay),0), COALESCE(SUM(PlayDuration),0) FROM sessions";
        if (days.HasValue) command.Parameters.AddWithValue("$since", Since(days.Value));
        await using var row = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        await row.ReadAsync(token).ConfigureAwait(false);
        return new PlaybackSummary(row.GetInt32(0), row.GetInt64(1));
    }

    private static async Task<PlaybackSummary> SummaryRangeAsync(
        SqliteConnection db,
        int fromDays,
        int toDays,
        CancellationToken token)
    {
        await using var command = db.CreateCommand();
        command.CommandText = SessionCte("WHERE DateCreated >= $from AND DateCreated < $to") +
            " SELECT COALESCE(SUM(NewPlay),0), COALESCE(SUM(PlayDuration),0) FROM sessions";
        command.Parameters.AddWithValue("$from", Since(fromDays));
        command.Parameters.AddWithValue("$to", Since(toDays));
        await using var row = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        await row.ReadAsync(token).ConfigureAwait(false);
        return new PlaybackSummary(row.GetInt32(0), row.GetInt64(1));
    }

    private static async Task<IReadOnlyList<RankingItem>> TopMoviesAsync(SqliteConnection db, int? days, CancellationToken token)
    {
        await using var command = db.CreateCommand();
        var where = days.HasValue
            ? "WHERE DateCreated >= $since AND ItemType = 'Movie'"
            : "WHERE ItemType = 'Movie'";
        command.CommandText = SessionCte(where) + """
            SELECT ItemId, ItemName, SUM(NewPlay), SUM(PlayDuration), COUNT(DISTINCT UserId)
            FROM sessions GROUP BY ItemId, ItemName
            ORDER BY SUM(NewPlay) DESC, SUM(PlayDuration) DESC LIMIT 10
            """;
        if (days.HasValue) command.Parameters.AddWithValue("$since", Since(days.Value));
        return await ReadRankingsAsync(command, token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<RankingItem>> TopSeriesAsync(SqliteConnection db, int? days, CancellationToken token)
    {
        await using var command = db.CreateCommand();
        var where = days.HasValue
            ? "WHERE DateCreated >= $since AND ItemType = 'Episode'"
            : "WHERE ItemType = 'Episode'";
        command.CommandText = SessionCte(where) + """
            SELECT ItemId, ItemName, UserId, SUM(NewPlay), SUM(PlayDuration)
            FROM sessions GROUP BY ItemId, ItemName, UserId
            """;
        if (days.HasValue) command.Parameters.AddWithValue("$since", Since(days.Value));
        var series = new Dictionary<string, (string Id, string Name, int Plays, long Duration, HashSet<string> Users)>();
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            var episodeId = rows.GetString(0);
            var episodeName = rows.GetString(1);
            var item = Guid.TryParse(episodeId, out var id) ? _library.GetItemById(id) as Episode : null;
            var seriesId = item?.Series?.Id.ToString("N") ?? string.Empty;
            var seriesName = item?.Series?.Name ?? FallbackSeriesName(episodeName);
            var key = seriesId.Length > 0 ? seriesId : seriesName.ToLowerInvariant();
            if (!series.TryGetValue(key, out var aggregate))
            {
                aggregate = (seriesId, seriesName, 0, 0, new HashSet<string>());
            }

            aggregate.Plays += rows.GetInt32(3);
            aggregate.Duration += rows.GetInt64(4);
            aggregate.Users.Add(rows.GetString(2));
            series[key] = aggregate;
        }

        return series.Values
            .Select(x => new RankingItem(x.Id, x.Name, x.Plays, x.Duration, x.Users.Count))
            .OrderByDescending(x => x.Plays).ThenByDescending(x => x.DurationSeconds).Take(10).ToList();
    }

    private async Task<IReadOnlyList<RankingItem>> TopUsersAsync(SqliteConnection db, int? days, CancellationToken token)
    {
        var names = _users.GetUsers().ToDictionary(x => x.Id.ToString("N"), x => x.Username);
        await using var command = db.CreateCommand();
        command.CommandText = SessionCte(days.HasValue ? "WHERE DateCreated >= $since" : string.Empty) + """
            SELECT UserId, SUM(NewPlay), SUM(PlayDuration)
            FROM sessions GROUP BY UserId
            ORDER BY SUM(PlayDuration) DESC, SUM(NewPlay) DESC LIMIT 10
            """;
        if (days.HasValue) command.Parameters.AddWithValue("$since", Since(days.Value));
        var result = new List<RankingItem>();
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            var id = rows.GetString(0);
            result.Add(new RankingItem(id, names.GetValueOrDefault(id, "Unknown user"), rows.GetInt32(1), rows.GetInt64(2)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<NameCount>> CountsAsync(
        SqliteConnection db, string expression, int days, int limit, CancellationToken token)
    {
        await using var command = db.CreateCommand();
        command.CommandText = $"SELECT COALESCE(NULLIF({expression}, ''), 'Unknown'), COUNT(*) FROM PlaybackActivity WHERE DateCreated >= $since GROUP BY 1 ORDER BY 2 DESC LIMIT {limit}";
        command.Parameters.AddWithValue("$since", Since(days));
        var result = new List<NameCount>();
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            result.Add(new NameCount(rows.GetString(0), rows.GetInt32(1)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<NameCount>> PlaybackMethodsAsync(
        SqliteConnection db,
        int days,
        CancellationToken token)
    {
        await using var command = db.CreateCommand();
        command.CommandText = """
            SELECT
                CASE
                    WHEN LOWER(COALESCE(PlaybackMethod, '')) LIKE 'transcode%' THEN 'Transcodes'
                    WHEN LOWER(REPLACE(COALESCE(PlaybackMethod, ''), ' ', '')) = 'directplay' THEN 'Direct play'
                    WHEN LOWER(REPLACE(COALESCE(PlaybackMethod, ''), ' ', '')) = 'directstream' THEN 'Direct stream'
                    ELSE 'Unknown'
                END AS Method,
                COUNT(*)
            FROM PlaybackActivity
            WHERE DateCreated >= $since
            GROUP BY Method
            ORDER BY COUNT(*) DESC
            """;
        command.Parameters.AddWithValue("$since", Since(days));
        var result = new List<NameCount>();
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            result.Add(new NameCount(rows.GetString(0), rows.GetInt32(1)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<DailyActivity>> ActivityAsync(SqliteConnection db, int days, CancellationToken token)
    {
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-(days - 1)));
        var result = Enumerable.Range(0, days)
            .Select(offset => start.AddDays(offset))
            .ToDictionary(
            date => date,
            date => new DailyActivity(date, 0, 0));
        await using var command = db.CreateCommand();
        command.CommandText = """
            SELECT DATE(DateCreated), COUNT(*), COALESCE(SUM(PlayDuration),0)
            FROM PlaybackActivity WHERE DateCreated >= $since
            GROUP BY DATE(DateCreated) ORDER BY DATE(DateCreated)
            """;
        command.Parameters.AddWithValue("$since", Since(days));
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            if (DateOnly.TryParse(rows.GetString(0), out var date) && result.ContainsKey(date))
            {
                result[date] = new DailyActivity(date, rows.GetInt32(1), rows.GetInt64(2));
            }
        }

        return result.Values.OrderBy(x => x.Date).ToList();
    }

    private static async Task<IReadOnlyDictionary<string, PersonalAnalytics>> PersonalAnalyticsAsync(
        SqliteConnection db,
        CancellationToken token)
    {
        await using var command = db.CreateCommand();
        command.CommandText = SessionCte() + """
            SELECT REPLACE(LOWER(UserId), '-', ''),
                   COALESCE(SUM(CASE WHEN DateCreated >= $since30 AND ItemType = 'Movie' THEN NewPlay ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN DateCreated >= $since30 AND ItemType = 'Episode' THEN NewPlay ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN DateCreated >= $since30 THEN PlayDuration ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN DateCreated >= $since365 AND ItemType = 'Movie' THEN NewPlay ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN DateCreated >= $since365 AND ItemType = 'Episode' THEN NewPlay ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN DateCreated >= $since365 THEN PlayDuration ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN ItemType = 'Movie' THEN NewPlay ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN ItemType = 'Episode' THEN NewPlay ELSE 0 END), 0),
                   COALESCE(SUM(PlayDuration), 0)
            FROM sessions
            GROUP BY REPLACE(LOWER(UserId), '-', '')
            """;
        command.Parameters.AddWithValue("$since30", Since(30));
        command.Parameters.AddWithValue("$since365", Since(365));
        var result = new Dictionary<string, PersonalAnalytics>(StringComparer.OrdinalIgnoreCase);
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            result[rows.GetString(0)] = new PersonalAnalytics(
                new PersonalPeriod(rows.GetInt32(1), rows.GetInt32(2), rows.GetInt64(3)),
                new PersonalPeriod(rows.GetInt32(4), rows.GetInt32(5), rows.GetInt64(6)),
                new PersonalPeriod(rows.GetInt32(7), rows.GetInt32(8), rows.GetInt64(9)),
                new ViewingHabits("–", "–", 0, 0, 0),
                new ViewingHabits("–", "–", 0, 0, 0),
                new ViewingHabits("–", "–", 0, 0, 0));
        }

        await AddPersonalHabitsAsync(db, result, token).ConfigureAwait(false);
        return result;
    }

    private static async Task AddPersonalHabitsAsync(
        SqliteConnection db,
        Dictionary<string, PersonalAnalytics> result,
        CancellationToken token)
    {
        var habits30 = await CalculateHabitsAsync(db, 30, token).ConfigureAwait(false);
        var habits365 = await CalculateHabitsAsync(db, 365, token).ConfigureAwait(false);
        var habitsAll = await CalculateHabitsAsync(db, null, token).ConfigureAwait(false);

        foreach (var (id, analytics) in result.ToList())
        {
            result[id] = analytics with
            {
                Habits30Days = CreateHabits(habits30.GetValueOrDefault(id), analytics.Last30Days),
                HabitsLastYear = CreateHabits(habits365.GetValueOrDefault(id), analytics.LastYear),
                HabitsAllTime = CreateHabits(habitsAll.GetValueOrDefault(id), analytics.AllTime)
            };
        }
    }

    private static ViewingHabits CreateHabits(
        (string? Weekday, string? TimeOfDay, long LongestSession) values,
        PersonalPeriod period)
    {
        var total = period.Movies + period.Episodes;
        var moviePercent = total == 0 ? 0 : (int)Math.Round(period.Movies * 100d / total);
        return new ViewingHabits(
            values.Weekday ?? "–",
            values.TimeOfDay ?? "–",
            values.LongestSession,
            moviePercent,
            total == 0 ? 0 : 100 - moviePercent);
    }

    private static async Task<Dictionary<string, (string? Weekday, string? TimeOfDay, long LongestSession)>>
        CalculateHabitsAsync(SqliteConnection db, int? days, CancellationToken token)
    {
        var where = days.HasValue ? "WHERE DateCreated >= $since" : string.Empty;
        var weekdays = new Dictionary<string, (string Name, int Count)>();
        await using (var command = db.CreateCommand())
        {
            command.CommandText = SessionCte(where) + """
                SELECT REPLACE(LOWER(UserId), '-', ''), strftime('%w', DateCreated), SUM(NewPlay)
                FROM sessions
                GROUP BY REPLACE(LOWER(UserId), '-', ''), strftime('%w', DateCreated)
                ORDER BY 3 DESC
                """;
            if (days.HasValue) command.Parameters.AddWithValue("$since", Since(days.Value));
            await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var names = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            while (await rows.ReadAsync(token).ConfigureAwait(false))
            {
                var id = rows.GetString(0);
                var count = rows.GetInt32(2);
                if (!weekdays.TryGetValue(id, out var current) || count > current.Count)
                {
                    weekdays[id] = (names[int.Parse(rows.GetString(1), CultureInfo.InvariantCulture)], count);
                }
            }
        }

        var times = new Dictionary<string, (string Name, int Count)>();
        await using (var command = db.CreateCommand())
        {
            command.CommandText = SessionCte(where) + """
                SELECT REPLACE(LOWER(UserId), '-', ''),
                       CASE
                           WHEN CAST(strftime('%H', DateCreated) AS INTEGER) < 6 THEN 'Night · 00–06'
                           WHEN CAST(strftime('%H', DateCreated) AS INTEGER) < 12 THEN 'Morning · 06–12'
                           WHEN CAST(strftime('%H', DateCreated) AS INTEGER) < 18 THEN 'Afternoon · 12–18'
                           ELSE 'Evening · 18–24'
                       END,
                       SUM(NewPlay)
                FROM sessions
                GROUP BY REPLACE(LOWER(UserId), '-', ''), 2
                ORDER BY 3 DESC
                """;
            if (days.HasValue) command.Parameters.AddWithValue("$since", Since(days.Value));
            await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rows.ReadAsync(token).ConfigureAwait(false))
            {
                var id = rows.GetString(0);
                var count = rows.GetInt32(2);
                if (!times.TryGetValue(id, out var current) || count > current.Count)
                {
                    times[id] = (rows.GetString(1), count);
                }
            }
        }

        var longest = new Dictionary<string, long>();
        await using (var command = db.CreateCommand())
        {
            command.CommandText = $$"""
                WITH ordered AS (
                    SELECT DateCreated, UserId, COALESCE(PlayDuration, 0) AS PlayDuration,
                           LAG(DateCreated) OVER (PARTITION BY UserId ORDER BY DateCreated) AS PreviousDate
                    FROM PlaybackActivity
                    {{where}}
                ),
                numbered AS (
                    SELECT *,
                           SUM(CASE WHEN PreviousDate IS NULL
                                    OR (julianday(DateCreated) - julianday(PreviousDate)) * 86400 > {{SessionGapSeconds}}
                                    THEN 1 ELSE 0 END)
                           OVER (PARTITION BY UserId ORDER BY DateCreated) AS SessionNumber
                    FROM ordered
                ),
                totals AS (
                    SELECT REPLACE(LOWER(UserId), '-', '') AS NormalizedUserId,
                           SessionNumber,
                           SUM(PlayDuration) AS Duration
                    FROM numbered
                    GROUP BY NormalizedUserId, SessionNumber
                )
                SELECT NormalizedUserId, MAX(Duration) FROM totals GROUP BY NormalizedUserId
                """;
            if (days.HasValue) command.Parameters.AddWithValue("$since", Since(days.Value));
            await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rows.ReadAsync(token).ConfigureAwait(false))
            {
                longest[rows.GetString(0)] = rows.GetInt64(1);
            }
        }

        var ids = weekdays.Keys.Concat(times.Keys).Concat(longest.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, (string? Weekday, string? TimeOfDay, long LongestSession)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            result[id] = (
                weekdays.GetValueOrDefault(id).Name,
                times.GetValueOrDefault(id).Name,
                longest.GetValueOrDefault(id));
        }

        return result;
    }

    private async Task<IReadOnlyList<TrendingItem>> TrendingAsync(
        SqliteConnection db,
        CancellationToken token)
    {
        await using var command = db.CreateCommand();
        command.CommandText = SessionCte("WHERE DateCreated >= $since14 AND ItemType IN ('Movie', 'Episode')") + """
            SELECT ItemId, ItemName, ItemType, UserId, date(DateCreated),
                   CASE WHEN DateCreated >= $since7 THEN 1 ELSE 0 END,
                   SUM(NewPlay)
            FROM sessions
            GROUP BY ItemId, ItemName, ItemType, UserId, date(DateCreated),
                     CASE WHEN DateCreated >= $since7 THEN 1 ELSE 0 END
            """;
        command.Parameters.AddWithValue("$since14", Since(14));
        command.Parameters.AddWithValue("$since7", Since(7));
        var items = new Dictionary<string, TrendAggregate>();
        var seriesByEpisode = new Dictionary<string, (string Id, string Name)>();
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            var itemId = rows.GetString(0);
            var itemName = rows.GetString(1);
            var type = rows.GetString(2);
            var userId = rows.GetString(3);
            var day = rows.GetString(4);
            var isCurrent = rows.GetInt32(5) == 1;
            var plays = rows.GetInt32(6);
            if (type == "Episode")
            {
                if (!seriesByEpisode.TryGetValue(itemId, out var series))
                {
                    var episode = Guid.TryParse(itemId, out var id) ? _library.GetItemById(id) as Episode : null;
                    series = (
                        episode?.Series?.Id.ToString("N") ?? string.Empty,
                        episode?.Series?.Name ?? FallbackSeriesName(itemName));
                    seriesByEpisode[itemId] = series;
                }

                itemId = series.Id;
                itemName = series.Name;
                type = "Series";
            }
            else
            {
                type = "Movie";
            }

            var key = itemId.Length > 0 ? $"{type}:{itemId}" : $"{type}:{itemName.ToLowerInvariant()}";
            if (!items.TryGetValue(key, out var aggregate))
            {
                aggregate = new TrendAggregate(itemId, itemName, type);
                items[key] = aggregate;
            }

            aggregate.Add(userId, day, plays, isCurrent);
        }

        return items.Values
            .Where(x => x.UniqueViewers >= 2)
            .OrderByDescending(x => x.UniqueViewers)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.CurrentActivity - x.PreviousActivity)
            .ThenByDescending(x => x.CurrentActivity)
            .Take(16)
            .Select(x => new TrendingItem(
                x.Id,
                x.Name,
                x.Type,
                x.CurrentActivity,
                x.PreviousActivity,
                x.UniqueViewers,
                x.ActiveDays))
            .ToList();
    }

    private sealed class TrendAggregate
    {
        private readonly Dictionary<(string UserId, string Day), int> _current = new();
        private readonly Dictionary<(string UserId, string Day), int> _previous = new();

        public TrendAggregate(string id, string name, string type)
        {
            Id = id;
            Name = name;
            Type = type;
        }

        public string Id { get; }

        public string Name { get; }

        public string Type { get; }

        public int CurrentActivity => Activity(_current);

        public int PreviousActivity => Activity(_previous);

        public int UniqueViewers => _current.Keys.Select(key => key.UserId).Distinct().Count();

        public int ActiveDays => _current.Keys.Select(key => key.Day).Distinct().Count();

        public int Score =>
            (ActiveDays * 2)
            + CurrentActivity
            + (Math.Min(10, Math.Max(0, CurrentActivity - PreviousActivity)) * 2);

        public void Add(string userId, string day, int plays, bool current)
        {
            var periods = current ? _current : _previous;
            var key = (userId, day);
            periods[key] = periods.GetValueOrDefault(key) + plays;
        }

        private int Activity(Dictionary<(string UserId, string Day), int> periods)
        {
            var dailyCap = Type == "Series" ? 2 : 1;
            return periods.Values.Sum(value => Math.Min(dailyCap, value));
        }
    }

    private static async Task<IReadOnlyList<RankingItem>> ReadRankingsAsync(SqliteCommand command, CancellationToken token)
    {
        var result = new List<RankingItem>();
        await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await rows.ReadAsync(token).ConfigureAwait(false))
        {
            result.Add(new RankingItem(rows.GetString(0), rows.GetString(1), rows.GetInt32(2), rows.GetInt64(3), rows.GetInt32(4)));
        }

        return result;
    }

    private static async Task ExecuteAsync(SqliteConnection db, string sql, CancellationToken token)
    {
        await using var command = db.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static string FallbackSeriesName(string episodeName)
    {
        var marker = System.Text.RegularExpressions.Regex.Match(
            episodeName,
            @"\s+-\s+s\d{1,3}e\d{1,3}\s+-\s+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return marker.Success ? episodeName[..marker.Index].Trim() : episodeName.Trim();
    }
}

public sealed record PlaybackAnalytics(
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
    IReadOnlyDictionary<string, PersonalAnalytics> Personal);
