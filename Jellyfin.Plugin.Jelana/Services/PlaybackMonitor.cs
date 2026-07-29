using System.Collections.Concurrent;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jelana.Services;

public sealed class PlaybackMonitor : IHostedService
{
    private sealed class ActivePlayback
    {
        public required DateTimeOffset StartedAt { get; init; }
        public required DateTimeOffset LastChangedAt { get; set; }
        public required string UserId { get; init; }
        public required string UserName { get; init; }
        public required string ItemId { get; init; }
        public required string ItemName { get; init; }
        public required string ItemType { get; init; }
        public required string Client { get; init; }
        public required string Device { get; init; }
        public required string PlaybackMethod { get; init; }
        public bool IsPaused { get; set; }
        public double PlayedSeconds { get; set; }
    }

    private readonly ConcurrentDictionary<string, ActivePlayback> _active = new();
    private readonly ISessionManager _sessions;
    private readonly PlaybackStore _store;
    private readonly ILogger<PlaybackMonitor> _logger;

    public PlaybackMonitor(ISessionManager sessions, PlaybackStore store, ILogger<PlaybackMonitor> logger)
    {
        _sessions = sessions;
        _store = store;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart += OnStart;
        _sessions.PlaybackProgress += OnProgress;
        _sessions.PlaybackStopped += OnStop;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart -= OnStart;
        _sessions.PlaybackProgress -= OnProgress;
        _sessions.PlaybackStopped -= OnStop;
        return Task.CompletedTask;
    }

    private static string Key(string deviceId, Guid userId, Guid itemId) =>
        $"{deviceId}:{userId:N}:{itemId:N}";

    private void OnStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is null || e.Item.IsThemeMedia || e.Users.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        var user = e.Users[0];
        var method = e.Session?.PlayState?.PlayMethod?.ToString()
            ?? PlayMethod.DirectPlay.ToString();
        var itemId = e.Item.Id;
        var itemName = e.Item.Name;
        if (e.Item is Episode episode)
        {
            itemId = episode.Series?.Id ?? e.Item.Id;
            itemName = episode.Series?.Name ?? e.Item.Name;
        }
        _active[Key(e.DeviceId, user.Id, e.Item.Id)] = new ActivePlayback
        {
            StartedAt = now,
            LastChangedAt = now,
            UserId = user.Id.ToString("N"),
            UserName = user.Username,
            ItemId = itemId.ToString("N"),
            ItemName = itemName,
            ItemType = e.Item.GetType().Name,
            Client = e.ClientName,
            Device = e.DeviceName,
            PlaybackMethod = method,
            IsPaused = e.IsPaused
        };
    }

    private void OnProgress(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is null || e.Users.Count == 0) return;
        if (!_active.TryGetValue(Key(e.DeviceId, e.Users[0].Id, e.Item.Id), out var playback)) return;
        UpdateDuration(playback, e.IsPaused);
    }

    private async void OnStop(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Item is null || e.Users.Count == 0) return;
        if (!_active.TryRemove(Key(e.DeviceId, e.Users[0].Id, e.Item.Id), out var playback)) return;
        UpdateDuration(playback, true);
        if (playback.PlayedSeconds < 20) return;
        try
        {
            await _store.AppendAsync(new Models.PlaybackRecord(
                playback.StartedAt,
                (int)Math.Round(playback.PlayedSeconds),
                playback.UserId,
                playback.UserName,
                playback.ItemId,
                playback.ItemName,
                playback.ItemType,
                playback.Client,
                playback.Device,
                playback.PlaybackMethod), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not persist Jelana playback event.");
        }
    }

    private static void UpdateDuration(ActivePlayback playback, bool isPaused)
    {
        var now = DateTimeOffset.UtcNow;
        if (!playback.IsPaused)
        {
            playback.PlayedSeconds += Math.Max(0, (now - playback.LastChangedAt).TotalSeconds);
        }

        playback.IsPaused = isPaused;
        playback.LastChangedAt = now;
    }
}
