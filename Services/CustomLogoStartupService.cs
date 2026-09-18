using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomLogo.Services
{
    /// <summary>
    /// Applies the generated CSS on server start and whenever the plugin configuration is saved.
    /// </summary>
    public sealed class CustomLogoStartupService : IHostedService, IDisposable
    {
        private readonly BrandingCssService _brandingCssService;
        private readonly ILogger<CustomLogoStartupService> _logger;
        private bool _subscribed;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomLogoStartupService"/> class.
        /// </summary>
        /// <param name="brandingCssService">The branding CSS writer.</param>
        /// <param name="logger">The logger.</param>
        public CustomLogoStartupService(BrandingCssService brandingCssService, ILogger<CustomLogoStartupService> logger)
        {
            _brandingCssService = brandingCssService;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var plugin = Plugin.Instance;
            if (plugin is not null && !_subscribed)
            {
                plugin.ConfigurationChanged += OnConfigurationChanged;
                _subscribed = true;
            }

            // Re-assert the block at start-up: the administrator may have edited custom CSS by
            // hand, or restored a backup, while the server was down.
            _brandingCssService.Synchronize();
            _logger.LogDebug("Custom Logo: startup synchronization complete.");

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Unsubscribe();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Unsubscribe();
            _disposed = true;
        }

        private void Unsubscribe()
        {
            var plugin = Plugin.Instance;
            if (plugin is not null && _subscribed)
            {
                plugin.ConfigurationChanged -= OnConfigurationChanged;
                _subscribed = false;
            }
        }

        private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
        {
            _brandingCssService.Synchronize();
        }
    }
}
