using Jellyfin.Plugin.Jelana.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Jelana;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<PlaybackReportingReader>();
        services.AddSingleton<LibraryAnalyticsReader>();
        services.AddSingleton<SnapshotStore>();
    }
}
