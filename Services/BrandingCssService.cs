using System;
using System.Globalization;
using System.Text;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Branding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomLogo.Services
{
    /// <summary>
    /// Writes the plugin's generated CSS into the server's branding configuration.
    /// </summary>
    /// <remarks>
    /// Jellyfin serves <see cref="BrandingOptions.CustomCss"/> to every client (including the
    /// unauthenticated login page) via <c>/Branding/Css</c>. Injecting there is the only
    /// supported way to restyle the web client that survives a server or web-client update,
    /// and the mechanism is unchanged between 10.11.x and 12.0.x.
    /// </remarks>
    public sealed class BrandingCssService
    {
        /// <summary>
        /// The configuration store key for <see cref="BrandingOptions"/>.
        /// </summary>
        public const string BrandingConfigKey = "branding";

        private const string BeginMarker = "/* BEGIN jellyfin-plugin-customlogo - do not edit inside this block */";
        private const string EndMarker = "/* END jellyfin-plugin-customlogo */";

        private readonly IConfigurationManager _configurationManager;
        private readonly ILogger<BrandingCssService> _logger;
        private readonly object _lock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="BrandingCssService"/> class.
        /// </summary>
        /// <param name="configurationManager">The server configuration manager.</param>
        /// <param name="logger">The logger.</param>
        public BrandingCssService(IConfigurationManager configurationManager, ILogger<BrandingCssService> logger)
        {
            _configurationManager = configurationManager;
            _logger = logger;
        }

        /// <summary>
        /// Rebuilds the managed CSS block from the current plugin configuration and stores it.
        /// </summary>
        public void Synchronize()
        {
            var plugin = Plugin.Instance;
            if (plugin is null)
            {
                return;
            }

            var block = BuildBlock(plugin.Configuration);

            lock (_lock)
            {
                try
                {
                    var branding = _configurationManager.GetConfiguration<BrandingOptions>(BrandingConfigKey);
                    var existing = branding.CustomCss ?? string.Empty;
                    var updated = ReplaceManagedBlock(existing, block);

                    if (string.Equals(existing, updated, StringComparison.Ordinal))
                    {
                        return;
                    }

                    branding.CustomCss = updated;
                    _configurationManager.SaveConfiguration(BrandingConfigKey, branding);
                    _logger.LogInformation(
                        "Custom Logo: branding CSS updated ({State}).",
                        block.Length == 0 ? "cleared" : "applied");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Custom Logo: failed to update branding CSS.");
                }
            }
        }

        /// <summary>
        /// Removes the managed CSS block, leaving any user authored CSS untouched.
        /// </summary>
        public void Remove()
        {
            lock (_lock)
            {
                try
                {
                    var branding = _configurationManager.GetConfiguration<BrandingOptions>(BrandingConfigKey);
                    var existing = branding.CustomCss ?? string.Empty;
                    var updated = ReplaceManagedBlock(existing, string.Empty);

                    if (string.Equals(existing, updated, StringComparison.Ordinal))
                    {
                        return;
                    }

                    branding.CustomCss = updated;
                    _configurationManager.SaveConfiguration(BrandingConfigKey, branding);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Custom Logo: failed to remove branding CSS.");
                }
            }
        }

        /// <summary>
        /// Builds the CSS the plugin manages. Returns an empty string when nothing should be applied.
        /// </summary>
        /// <param name="config">The plugin configuration.</param>
        /// <returns>The CSS rules, without the surrounding markers.</returns>
        public static string BuildCss(PluginConfiguration config)
        {
            if (config is null || !config.Enabled)
            {
                return string.Empty;
            }

            var logo = (config.LogoDataUri ?? string.Empty).Trim();
            var hasLogo = logo.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
            var extra = (config.ExtraCss ?? string.Empty).Trim();

            if (!hasLogo && extra.Length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            if (hasLogo)
            {
                // CSS url() tokens must not contain a raw quote or newline.
                var url = logo.Replace("\"", "%22", StringComparison.Ordinal)
                              .Replace("\r", string.Empty, StringComparison.Ordinal)
                              .Replace("\n", string.Empty, StringComparison.Ordinal);

                if (config.ReplaceHeaderLogo)
                {
                    var width = SanitizeLength(config.HeaderLogoWidth, "13.2em");
                    var toolbarHeight = SanitizeLength(config.ModernHeaderLogoHeight, "1.75em");
                    var drawerHeight = SanitizeLength(config.ModernDrawerLogoHeight, "2.5rem");

                    // Legacy web client (all of 10.11, and the "legacy" app in 12.x).
                    // .pageTitleWithDefaultLogo takes its image from the active theme
                    // stylesheet, so the override has to be !important to win regardless
                    // of stylesheet order.
                    sb.Append(CultureInfo.InvariantCulture, $@"
.pageTitleWithDefaultLogo,
.layout-tv .pageTitleWithDefaultLogo {{
    background-image: url(""{url}"") !important;
    background-position: left center !important;
    background-repeat: no-repeat !important;
    background-size: contain !important;
    width: {width} !important;
}}

[dir=""rtl""] .pageTitleWithDefaultLogo {{
    background-position: right center !important;
}}
");

                    // Modern web client (12.x) and the dashboard. Here the logo is an
                    // <img> rendered by ServerButton and DrawerHeaderLink, not a CSS
                    // background, so the bitmap is swapped with content: url(). Those
                    // components carry no class of their own; the src is the stable hook,
                    // because webpack emits the asset as icon-transparent.<hash>.png.
                    // !important is required to beat MUI's inline max-height/max-width.
                    sb.Append(CultureInfo.InvariantCulture, $@"
img[src*=""icon-transparent""] {{
    content: url(""{url}"") !important;
    height: {toolbarHeight} !important;
    width: auto !important;
    max-height: none !important;
    max-width: none !important;
    object-fit: contain !important;
}}

.MuiListItemIcon-root img[src*=""icon-transparent""] {{
    height: {drawerHeight} !important;
}}
");
                }
            }

            if (extra.Length > 0)
            {
                sb.Append('\n').Append(extra).Append('\n');
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Replaces (or inserts, or removes) the plugin's marked block inside a CSS document.
        /// </summary>
        /// <param name="existingCss">The current custom CSS.</param>
        /// <param name="newBlockBody">The new block body; empty removes the block.</param>
        /// <returns>The resulting CSS.</returns>
        public static string ReplaceManagedBlock(string existingCss, string newBlockBody)
        {
            existingCss ??= string.Empty;

            var start = existingCss.IndexOf(BeginMarker, StringComparison.Ordinal);
            var end = existingCss.IndexOf(EndMarker, StringComparison.Ordinal);

            string before;
            string after;

            if (start >= 0 && end > start)
            {
                before = existingCss.Substring(0, start);
                after = existingCss.Substring(end + EndMarker.Length);
            }
            else if (start >= 0)
            {
                // Begin marker without a matching end marker: drop everything from it onwards
                // rather than nesting a second block on every save.
                before = existingCss.Substring(0, start);
                after = string.Empty;
            }
            else
            {
                before = existingCss;
                after = string.Empty;
            }

            before = before.TrimEnd();
            after = after.TrimStart();

            if (string.IsNullOrWhiteSpace(newBlockBody))
            {
                if (before.Length == 0)
                {
                    return after;
                }

                return after.Length == 0 ? before : before + "\n\n" + after;
            }

            var block = BeginMarker + "\n" + newBlockBody.Trim() + "\n" + EndMarker;

            var sb = new StringBuilder();
            if (before.Length > 0)
            {
                sb.Append(before).Append("\n\n");
            }

            sb.Append(block);

            if (after.Length > 0)
            {
                sb.Append("\n\n").Append(after);
            }

            return sb.ToString();
        }

        private static string BuildBlock(PluginConfiguration config) => BuildCss(config);

        /// <summary>
        /// Accepts only a simple CSS length so a stray value cannot break out of the rule.
        /// </summary>
        private static string SanitizeLength(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > 16)
            {
                return fallback;
            }

            var digits = 0;
            var i = 0;

            for (; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                if (char.IsAsciiDigit(c))
                {
                    digits++;
                }
                else if (c != '.')
                {
                    break;
                }
            }

            if (digits == 0)
            {
                return fallback;
            }

            var unit = trimmed.Substring(i);
            switch (unit)
            {
                case "em":
                case "rem":
                case "px":
                case "vw":
                case "vh":
                case "%":
                    return trimmed;
                default:
                    return fallback;
            }
        }
    }
}
