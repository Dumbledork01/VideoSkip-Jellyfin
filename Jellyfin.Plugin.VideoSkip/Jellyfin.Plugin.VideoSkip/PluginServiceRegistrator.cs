using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.VideoSkip;

/// <summary>
/// Registers a named HttpClient for Exchange requests at startup.
/// Jellyfin discovers this via <see cref="IPluginServiceRegistrator"/>, which
/// requires a parameterless constructor.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("VideoSkip", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-VideoSkip-Plugin/1.0");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
    }
}
