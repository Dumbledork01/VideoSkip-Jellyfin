using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.VideoSkip.Configuration;

namespace Jellyfin.Plugin.VideoSkip;

/// <summary>
/// VideoSkip plugin entry point.
/// Exposes a /api/videoskip/{itemId} endpoint that the injected
/// JS client uses to fetch .skp files — either from a local sidecar
/// next to the video file, or from the VideoSkip Exchange.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "VideoSkip";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("4a8b3c2d-1e5f-6789-abcd-ef0123456789");

    /// <inheritdoc />
    public override string Description => "Automatically loads VideoSkip .skp filter files for Jellyfin media.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }
}
