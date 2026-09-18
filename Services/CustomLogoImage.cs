using System;

namespace Jellyfin.Plugin.CustomLogo.Services
{
    /// <summary>
    /// Helpers for the RFC 2397 data URI the logo is stored as.
    /// </summary>
    public static class CustomLogoImage
    {
        private const string Base64Suffix = ";base64";

        /// <summary>
        /// Parses a base64 image data URI into its media type and payload.
        /// </summary>
        /// <param name="dataUri">The data URI.</param>
        /// <param name="contentType">The parsed media type.</param>
        /// <param name="bytes">The decoded payload.</param>
        /// <returns><c>true</c> when the URI was a well formed base64 image data URI.</returns>
        public static bool TryParse(string? dataUri, out string contentType, out byte[] bytes)
        {
            contentType = string.Empty;
            bytes = Array.Empty<byte>();

            if (string.IsNullOrWhiteSpace(dataUri))
            {
                return false;
            }

            var value = dataUri.Trim();
            if (!value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var comma = value.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0)
            {
                return false;
            }

            // "data:" is 5 characters.
            var header = value.Substring(5, comma - 5);
            if (!header.EndsWith(Base64Suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            contentType = header.Substring(0, header.Length - Base64Suffix.Length);
            if (contentType.Length == 0)
            {
                return false;
            }

            var payload = value.Substring(comma + 1);
            var buffer = new byte[((payload.Length * 3) / 4) + 4];
            if (!Convert.TryFromBase64String(payload, buffer, out var written) || written == 0)
            {
                contentType = string.Empty;
                return false;
            }

            bytes = new byte[written];
            Array.Copy(buffer, bytes, written);
            return true;
        }

        /// <summary>
        /// Maps a media type to the file extension the web client should serve it under.
        /// </summary>
        /// <param name="contentType">The media type.</param>
        /// <returns>An extension including the leading dot.</returns>
        public static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            _ => ".png"
        };
    }
}
