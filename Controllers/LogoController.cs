using System;
using Jellyfin.Plugin.CustomLogo.Services;
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
        private readonly WebAssetPatcher _webAssetPatcher;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogoController"/> class.
        /// </summary>
        /// <param name="webAssetPatcher">The web client patcher.</param>
        public LogoController(WebAssetPatcher webAssetPatcher)
        {
            _webAssetPatcher = webAssetPatcher;
        }

        /// <summary>
        /// Reports whether the favicon and splash patch could be applied.
        /// </summary>
        /// <remarks>
        /// Deliberately free of file-system paths so it is safe to serve anonymously
        /// alongside the logo itself.
        /// </remarks>
        /// <response code="200">Status returned.</response>
        /// <returns>The patch status.</returns>
        [HttpGet("Status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult GetStatus()
        {
            return new JsonResult(new
            {
                WebPatchApplied = _webAssetPatcher.Applied,
                Message = _webAssetPatcher.Status
            });
        }

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
            if (config is null || !CustomLogoImage.TryParse(config.LogoDataUri, out var contentType, out var bytes))
            {
                return NotFound();
            }

            return File(bytes, contentType);
        }

    }
}
