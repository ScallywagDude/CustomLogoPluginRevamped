using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.CustomLogo.Controllers
{
    /// <summary>
    /// Serves the configured logo as a plain image.
    /// </summary>
    /// <remarks>
    /// The web client itself does not need this: the logo is embedded in the generated CSS as a
    /// data URI so it also renders on the unauthenticated login and splash screens. This endpoint
    /// exists so the same image can be referenced from a custom theme, a reverse proxy error page
    /// or an external dashboard. It is deliberately anonymous — a branding logo is public by
    /// nature, and the login screen is shown before any credentials exist.
    /// </remarks>
    [ApiController]
    [Route("CustomLogo")]
    [AllowAnonymous]
    public class LogoController : ControllerBase
    {
        /// <summary>
        /// Gets the configured logo image.
        /// </summary>
        /// <response code="200">Logo returned.</response>
        /// <response code="404">No logo configured.</response>
        /// <returns>The logo image.</returns>
        [HttpGet("Logo")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public ActionResult GetLogo()
        {
            var config = Plugin.Instance?.Configuration;
            if (config is null || !TryDecodeDataUri(config.LogoDataUri, out var contentType, out var bytes))
            {
                return NotFound();
            }

            return File(bytes, contentType);
        }

        /// <summary>
        /// Parses an RFC 2397 base64 data URI into its media type and payload.
        /// </summary>
        /// <param name="dataUri">The data URI.</param>
        /// <param name="contentType">The parsed media type.</param>
        /// <param name="bytes">The decoded payload.</param>
        /// <returns><c>true</c> when the URI was a well formed base64 image data URI.</returns>
        internal static bool TryDecodeDataUri(string? dataUri, out string contentType, out byte[] bytes)
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

            // "data:" == 5 characters.
            var header = value.Substring(5, comma - 5);
            if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            contentType = header.Substring(0, header.Length - ";base64".Length);
            if (contentType.Length == 0)
            {
                return false;
            }

            var payload = value.Substring(comma + 1);
            var buffer = new byte[((payload.Length * 3) / 4) + 4];
            if (!Convert.TryFromBase64String(payload, buffer, out var written) || written == 0)
            {
                return false;
            }

            bytes = new byte[written];
            Array.Copy(buffer, bytes, written);
            return true;
        }
    }
}
