using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomLogo.Services
{
    /// <summary>
    /// Patches the web client's <c>index.html</c> so the favicon and the start-up splash
    /// logo can be replaced.
    /// </summary>
    /// <remarks>
    /// Neither of those is reachable from branding CSS. The favicon is a <c>&lt;link&gt;</c>
    /// in the document head, which CSS cannot address at all, and the splash markup is
    /// discarded the moment React mounts — before the branding stylesheet is injected.
    /// Editing the served document is the only way to affect either.
    /// <para>
    /// The original <c>index.html</c> is copied into the plugin's data folder and every
    /// generated version is produced from that copy, so the transform is idempotent and
    /// fully reversible. If Jellyfin replaces the web client on update, the new file will
    /// not carry the plugin's marker, and it is adopted as the new original.
    /// </para>
    /// </remarks>
    public sealed class WebAssetPatcher
    {
        private const string BeginMarker = "<!-- BEGIN jellyfin-plugin-customlogo (generated; edits here are overwritten) -->";
        private const string EndMarker = "<!-- END jellyfin-plugin-customlogo -->";
        private const string BackupFileName = "index.html.original";
        private const string AssetPrefix = "customlogo-asset.";

        private static readonly Regex IconLinkRegex = new(
            "[ \t]*<link[^>]*rel=[\"'](?:shortcut icon|icon|apple-touch-icon)[\"'][^>]*>[ \t]*\r?\n?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TileImageRegex = new(
            "[ \t]*<meta[^>]*name=[\"']msapplication-TileImage[\"'][^>]*>[ \t]*\r?\n?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IApplicationPaths _paths;
        private readonly ILogger<WebAssetPatcher> _logger;
        private readonly object _lock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="WebAssetPatcher"/> class.
        /// </summary>
        /// <param name="paths">Application paths.</param>
        /// <param name="logger">The logger.</param>
        public WebAssetPatcher(IApplicationPaths paths, ILogger<WebAssetPatcher> logger)
        {
            _paths = paths;
            _logger = logger;
        }

        /// <summary>
        /// Gets a human readable description of the last attempt, for the configuration page.
        /// </summary>
        public string Status { get; private set; } = "Not applied yet.";

        /// <summary>
        /// Gets a value indicating whether the web client is currently patched.
        /// </summary>
        public bool Applied { get; private set; }

        /// <summary>
        /// Brings the web client into line with the current configuration.
        /// </summary>
        public void Synchronize()
        {
            var plugin = Plugin.Instance;
            if (plugin is null)
            {
                return;
            }

            var config = plugin.Configuration;
            var wanted = config.Enabled
                && (config.ReplaceFavicon || config.ReplaceSplashLogo)
                && CustomLogoImage.TryParse(config.LogoDataUri, out _, out _);

            lock (_lock)
            {
                try
                {
                    if (wanted)
                    {
                        ApplyInternal(config);
                    }
                    else
                    {
                        RemoveInternal();
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Applied = false;
                    Status = "The web client folder is not writable by the Jellyfin process, so the "
                        + "favicon and splash logo cannot be changed. The header logo still works. "
                        + "This is normal for package installs where the web files are owned by root.";
                    _logger.LogWarning(ex, "Custom Logo: no write access to the web client folder.");
                }
                catch (IOException ex)
                {
                    Applied = false;
                    Status = "Could not write to the web client folder: " + ex.Message;
                    _logger.LogWarning(ex, "Custom Logo: failed to patch the web client.");
                }
                catch (Exception ex)
                {
                    Applied = false;
                    Status = "Unexpected failure while patching the web client: " + ex.Message;
                    _logger.LogError(ex, "Custom Logo: failed to patch the web client.");
                }
            }
        }

        private string? WebRoot()
        {
            var web = _paths.WebPath;
            if (string.IsNullOrEmpty(web) || !Directory.Exists(web))
            {
                return null;
            }

            return File.Exists(Path.Combine(web, "index.html")) ? web : null;
        }

        private void ApplyInternal(PluginConfiguration config)
        {
            var web = WebRoot();
            if (web is null)
            {
                Applied = false;
                Status = "The web client folder could not be located, so the favicon and splash "
                    + "logo were left alone. The header logo is unaffected.";
                return;
            }

            if (!CustomLogoImage.TryParse(config.LogoDataUri, out var contentType, out var bytes))
            {
                RemoveInternal();
                return;
            }

            var indexPath = Path.Combine(web, "index.html");
            var backupPath = Path.Combine(_paths.PluginConfigurationsPath, BackupFileName);
            var original = ReadOriginal(indexPath, backupPath);

            // Content-addressed name: the browser refetches when the logo changes, which
            // matters because favicons are cached very aggressively.
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..8];
            var assetName = AssetPrefix + hash + CustomLogoImage.ExtensionFor(contentType);

            CleanAssets(web, assetName);
            File.WriteAllBytes(Path.Combine(web, assetName), bytes);

            var patched = Transform(original, config, assetName);
            if (!string.Equals(ReadAllTextOrEmpty(indexPath), patched, StringComparison.Ordinal))
            {
                File.WriteAllText(indexPath, patched, new UTF8Encoding(false));
            }

            Applied = true;
            Status = BuildAppliedStatus(config);
            _logger.LogInformation("Custom Logo: patched the web client ({Asset}).", assetName);
        }

        private void RemoveInternal()
        {
            var web = WebRoot();
            if (web is null)
            {
                Applied = false;
                Status = "Not applied.";
                return;
            }

            var indexPath = Path.Combine(web, "index.html");
            var backupPath = Path.Combine(_paths.PluginConfigurationsPath, BackupFileName);
            var current = ReadAllTextOrEmpty(indexPath);

            if (current.Contains(BeginMarker, StringComparison.Ordinal) && File.Exists(backupPath))
            {
                File.WriteAllText(indexPath, File.ReadAllText(backupPath), new UTF8Encoding(false));
                _logger.LogInformation("Custom Logo: restored the original web client index.html.");
            }

            CleanAssets(web, null);
            Applied = false;
            Status = "Not applied.";
        }

        /// <summary>
        /// Returns the pristine document, refreshing the stored copy when Jellyfin has
        /// replaced the web client since the last run.
        /// </summary>
        private static string ReadOriginal(string indexPath, string backupPath)
        {
            var current = File.ReadAllText(indexPath);

            if (!current.Contains(BeginMarker, StringComparison.Ordinal))
            {
                // Untouched by this plugin, so it is authoritative — a fresh install or a
                // web client that has just been updated underneath us.
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.WriteAllText(backupPath, current, new UTF8Encoding(false));
                return current;
            }

            if (File.Exists(backupPath))
            {
                return File.ReadAllText(backupPath);
            }

            // Marker present but the backup is gone: strip the block to recover the original.
            return StripBlock(current);
        }

        internal static string StripBlock(string html)
        {
            var start = html.IndexOf(BeginMarker, StringComparison.Ordinal);
            var end = html.IndexOf(EndMarker, StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                return html;
            }

            // Take the whole lines the markers sit on, including the indentation before the
            // opening marker and the newline after the closing one. Leaving either behind
            // would make the transform non-idempotent and stop a strip from restoring the
            // original byte-for-byte.
            while (start > 0 && (html[start - 1] == ' ' || html[start - 1] == '\t'))
            {
                start--;
            }

            var after = end + EndMarker.Length;
            if (after < html.Length && html[after] == '\r')
            {
                after++;
            }

            if (after < html.Length && html[after] == '\n')
            {
                after++;
            }

            return html.Remove(start, after - start);
        }

        internal static string Transform(string original, PluginConfiguration config, string assetName)
        {
            var html = StripBlock(original);

            if (config.ReplaceFavicon)
            {
                // Drop the stock icon declarations rather than racing them: which of several
                // same-rel links a browser honours is not consistently defined.
                html = IconLinkRegex.Replace(html, string.Empty);
                html = TileImageRegex.Replace(html, string.Empty);
            }

            var block = BuildBlock(config, assetName);

            var headClose = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headClose < 0)
            {
                // No head to inject into; leave the document untouched rather than guess.
                return html;
            }

            return html.Insert(headClose, block);
        }

        private static string BuildBlock(PluginConfiguration config, string assetName)
        {
            var sb = new StringBuilder();
            sb.Append("    ").Append(BeginMarker).Append('\n');

            if (config.ReplaceFavicon)
            {
                // Relative hrefs: index.html is served from the web root, so these keep
                // working behind a reverse proxy that mounts Jellyfin under a sub-path.
                sb.Append(CultureInfo.InvariantCulture, $"    <link rel=\"icon\" href=\"{assetName}\">\n");
                sb.Append(CultureInfo.InvariantCulture, $"    <link rel=\"shortcut icon\" href=\"{assetName}\">\n");
                sb.Append(CultureInfo.InvariantCulture, $"    <link rel=\"apple-touch-icon\" href=\"{assetName}\">\n");
                sb.Append(CultureInfo.InvariantCulture, $"    <meta name=\"msapplication-TileImage\" content=\"{assetName}\">\n");
            }

            if (config.ReplaceSplashLogo)
            {
                // site.scss sets .splashLogo twice (icon, then a banner above 992px), so the
                // override has to be !important to cover both.
                sb.Append(CultureInfo.InvariantCulture, $@"    <style>
        .splashLogo {{
            background-image: url(""{assetName}"") !important;
            background-position: center center !important;
            background-repeat: no-repeat !important;
            background-size: contain !important;
        }}
    </style>
");
            }

            sb.Append("    ").Append(EndMarker).Append('\n');
            return sb.ToString();
        }

        private static string BuildAppliedStatus(PluginConfiguration config)
        {
            var parts = new StringBuilder("Web client patched: ");
            if (config.ReplaceFavicon && config.ReplaceSplashLogo)
            {
                parts.Append("favicon and splash logo.");
            }
            else if (config.ReplaceFavicon)
            {
                parts.Append("favicon.");
            }
            else
            {
                parts.Append("splash logo.");
            }

            parts.Append(" A hard refresh may be needed; browsers cache favicons aggressively.");
            return parts.ToString();
        }

        private static void CleanAssets(string web, string? keep)
        {
            foreach (var file in Directory.EnumerateFiles(web, AssetPrefix + "*"))
            {
                var name = Path.GetFileName(file);
                if (keep is not null && string.Equals(name, keep, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // A stale asset is harmless; nothing references it any more.
                }
            }
        }

        private static string ReadAllTextOrEmpty(string path)
            => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
