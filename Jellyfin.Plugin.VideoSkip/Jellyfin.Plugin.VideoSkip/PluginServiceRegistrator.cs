using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.VideoSkip;

/// <summary>
/// Registers a named HttpClient for Exchange requests at startup.
/// </summary>
public class PluginServiceRegistrator
{
    /// <summary>
    /// Registers plugin services.
    /// </summary>
    public static void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpClient("VideoSkip", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-VideoSkip-Plugin/1.0");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
    }
}
