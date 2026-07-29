using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Jelana.Services;

public sealed class SnapshotTask : IScheduledTask
{
    private readonly SnapshotStore _snapshots;
    public SnapshotTask(SnapshotStore snapshots) => _snapshots = snapshots;
    public string Name => "Refresh Jelana analytics snapshot";
    public string Key => "Jelana.RefreshSnapshot";
    public string Description => "Builds the read-only analytics snapshot used by the Jelana UI.";
    public string Category => "Jelana";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(5);
        await _snapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(1).Ticks
        };
    }
}
