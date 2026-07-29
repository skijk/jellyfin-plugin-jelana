using System.Text.Json;
using Jellyfin.Plugin.Jelana.Models;

namespace Jellyfin.Plugin.Jelana.Services;

public sealed class PlaybackStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string EventsPath => Path.Combine(Plugin.Instance.DataFolderPath, "playback.ndjson");

    public async Task AppendAsync(PlaybackRecord record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Plugin.Instance.DataFolderPath);
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(EventsPath, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PlaybackRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(EventsPath))
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = new List<PlaybackRecord>();
            foreach (var line in await File.ReadAllLinesAsync(EventsPath, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<PlaybackRecord>(line, JsonOptions);
                    if (record is not null) records.Add(record);
                }
                catch (JsonException)
                {
                    // Preserve availability if one interrupted append produced a bad final line.
                }
            }

            return records;
        }
        finally
        {
            _gate.Release();
        }
    }
}
