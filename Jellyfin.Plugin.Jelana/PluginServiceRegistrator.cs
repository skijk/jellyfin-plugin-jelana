using Jellyfin.Plugin.Jelana.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Jelana;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<PlaybackStore>();
        services.AddSingleton<SnapshotStore>();
        services.AddHostedService<PlaybackMonitor>();
    }
}
