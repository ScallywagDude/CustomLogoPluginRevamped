using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CustomLogo
{
    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the custom logo is applied.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the logo encoded as an RFC 2397 data URI
        /// (for example <c>data:image/png;base64,iVBORw0...</c>).
        /// It is embedded directly in the generated CSS so that it renders on the
        /// login and splash screens, where no authenticated request can be made.
        /// </summary>
        public string LogoDataUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the original file name of the uploaded logo. Display only.
        /// </summary>
        public string LogoFileName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the header logo is replaced.
        /// </summary>
        public bool ReplaceHeaderLogo { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the splash/loading logo is replaced.
        /// </summary>
        public bool ReplaceSplashLogo { get; set; } = true;

        /// <summary>
        /// Gets or sets the CSS width used for the header logo, e.g. <c>13.2em</c>.
        /// </summary>
        public string HeaderLogoWidth { get; set; } = "13.2em";

        /// <summary>
        /// Gets or sets extra CSS appended inside the plugin's managed block.
        /// </summary>
        public string ExtraCss { get; set; } = string.Empty;
    }
}
