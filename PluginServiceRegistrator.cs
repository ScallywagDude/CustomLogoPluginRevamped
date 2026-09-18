using Jellyfin.Plugin.CustomLogo.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.CustomLogo
{
    /// <summary>
    /// Registers the plugin's services with the host.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<BrandingCssService>();
            serviceCollection.AddHostedService<CustomLogoStartupService>();
        }
    }
}
