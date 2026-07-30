using System.Globalization;
using Jellyfin.Plugin.Jelana.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Jelana;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public static Plugin Instance { get; private set; } = null!;
    public override string Name => "Jelana";
    public override string Description => "Cached Jellyfin analytics backed by Playback Reporting. JS Injector is optional for regular-user menu access.";
    public override Guid Id => Guid.Parse("e39f03aa-67c1-49f3-941e-491ce0ac47e5");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            DisplayName = "Analytics",
            EnableInMainMenu = true,
            MenuIcon = "analytics",
            EmbeddedResourcePath = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.Web.jelana.html",
                GetType().Namespace)
        };
    }
}
