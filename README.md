# Custom Logo — Jellyfin plugin

Replaces the Jellyfin logo in the web client header and on the loading / splash
screen with an image you upload.

**Install:** add this repository in **Dashboard → Plugins → Repositories**, then
install *Custom Logo* from the catalog.

```
https://raw.githubusercontent.com/ScallywagDude/CustomLogoPluginRevamped/main/manifest.json
```

Supported servers:

| Jellyfin       | Runtime  | Plugin build | targetAbi    |
| -------------- | -------- | ------------ | ------------ |
| 10.11.x        | .NET 9   | `2.0.1.0`    | `10.11.0.0`  |
| 12.0.x, 12.1.x | .NET 10  | `2.1.1.0`    | `12.0.0.0`   |

> **About "10.12"** — there is no Jellyfin 10.12. Jellyfin dropped the static
> leading `10.` from its version scheme, so what would have been 10.12.0 shipped
> as **12.0**. The `jf12.0` build below is the one to use on it.

The two builds share one source tree; they exist separately only because a single
assembly cannot target both .NET 9 and .NET 10. Every Jellyfin API this plugin
touches (`BasePlugin<T>`, `IHasWebPages`, `IPluginServiceRegistrator`,
`BrandingOptions`, `IConfigurationManager`) is byte-for-byte identical between
10.11.11 and 12.0.0, so there are no version conditionals in the code.

## How it works

Jellyfin serves the branding stylesheet from `/Branding/Css` to every client,
including the unauthenticated login page. This plugin writes its rules into that
stylesheet, inside a clearly delimited block:

```css
/* BEGIN jellyfin-plugin-customlogo - do not edit inside this block */
...generated rules...
/* END jellyfin-plugin-customlogo */
```

Anything you have written yourself in **Dashboard → General → Custom CSS**, before
or after that block, is preserved. Saving the plugin configuration rewrites only
the block; disabling the plugin or removing the logo deletes it and leaves your CSS
exactly as it was. The block is re-asserted at every server start, so a restored
backup or a hand edit cannot leave the branding half-applied.

The image is embedded in the CSS as a `data:` URI rather than referenced by URL.
That is deliberate: the login and splash screens render before any credentials
exist, and a relative URL in injected CSS resolves against the current page rather
than the stylesheet, which breaks under a reverse-proxy base path. An inline image
has neither problem.

The two interfaces draw the logo in completely different ways, so the plugin targets
both:

- **Legacy interface** (all of 10.11, and the `legacy` app in 12.x) draws it as a CSS
  background on `.pageTitleWithDefaultLogo`, set by the active *theme* stylesheet.
  The override is `!important` because the load order relative to branding CSS is not
  guaranteed.
- **Modern interface** (12.x) and the **dashboard** render it as an `<img>` — from
  `ServerButton` in the top bar and `DrawerHeaderLink` in the navigation drawer.
  CSS cannot change an `src`, so the bitmap is swapped with `content: url(...)`,
  supported on regular elements in Chromium, WebKit and Firefox 63+. Those components
  carry no class of their own, so the selector keys on the asset name,
  `img[src*="icon-transparent"]` — webpack emits it as `icon-transparent.<hash>.png`,
  preserving the name. `!important` is required to beat MUI's inline
  `max-height`/`max-width`.

There is deliberately no splash-screen option. The splash markup lives in `index.html`
and is replaced the moment React mounts, while branding CSS is injected by a React
component *after* mount — so a `.splashLogo` rule can never apply. Earlier versions
shipped that toggle; it did nothing.

Note that a user who ticks "Disable custom CSS" in their own display settings will not
see the custom logo, because Jellyfin skips the branding stylesheet for them.

## Installing

### From the plugin repository (recommended)

In Jellyfin: **Dashboard → Plugins → Repositories → +**, then enter

| Field | Value |
| --- | --- |
| Repository Name | `ScallywagDude` |
| Repository URL | `https://raw.githubusercontent.com/ScallywagDude/CustomLogoPluginRevamped/main/manifest.json` |

Save, then go to **Catalog**, find **Custom Logo** under *General*, and install it.
Restart Jellyfin when prompted.

The server reads `targetAbi` from the manifest and picks the build that matches it,
so 10.11 and 12.0 users both install from the same URL and neither can end up with
the wrong assembly. Updates appear in the catalog automatically once a new release
is published.

### Manually

1. Download the zip for your server from the
   [releases page](https://github.com/ScallywagDude/CustomLogoPluginRevamped/releases):
   `customlogo_2.0.1.0_jf10.11.zip` or `customlogo_2.1.1.0_jf12.0.zip`.
2. Extract it into `<data dir>/plugins/Custom Logo/` — the folder should contain
   `Jellyfin.Plugin.CustomLogo.dll` and `meta.json`.
3. Restart Jellyfin.

Common data directories: `/var/lib/jellyfin` (Linux package),
`/config` (Docker), `%LOCALAPPDATA%\jellyfin` (Windows).

## Using it

**Dashboard → Plugins → Custom Logo**

- **Logo image** — PNG, SVG, WebP, JPEG or GIF. A wide transparent PNG around
  400×60 px suits the header. Keep it well under 1 MB: it is inlined into a
  stylesheet every client downloads.
- **Enable custom logo** — turn off to restore stock branding without discarding
  the uploaded image.
- **Replace the header logo / splash logo** — toggle each independently.
- **Header logo width** — a CSS length (`13.2em`, `200px`, `15%`). Defaults to
  `13.2em`, matching the stock banner. Anything that is not a plain length is
  ignored in favour of the default.
- **Additional CSS** — appended inside the managed block, so it is removed cleanly
  along with everything else this plugin owns.

Save, then reload the web client with a hard refresh (Ctrl+F5) — browsers cache
the branding stylesheet.

### Endpoint

`GET /CustomLogo/Logo` returns the configured image with its real content type, or
404 when none is set. It is anonymous by design: a branding logo is public, and the
sign-in screen needs it before credentials exist. It is not used by the web client
(the CSS carries the image inline) and exists only so the same asset can be
referenced from a custom theme or an external page.

## Building

Requires the .NET 10 SDK, which also builds the .NET 9 target.

```
npm install
npm run package     # build both targets, write dist/*.zip, update manifest.json
npm run build       # compile only, no zips
```

`build.js` writes `meta.json` into each package, computes the MD5 digest Jellyfin
uses to verify repository downloads, and folds the results into `manifest.json` —
merging rather than overwriting, so previous releases stay available for rollback.
Rebuilding the same version and ABI replaces that record, checksum included.

All GitHub URLs derive from the `GITHUB_OWNER` / `GITHUB_REPO` constants at the top
of `build.js`. The release tag used in `sourceUrl` comes from `--tag`, or from
`GITHUB_REF_NAME` in CI, defaulting to `v2.0.0`.

The manifest lists the 12.0 build at the higher version number on purpose. On a
12.0 server both entries pass the ABI filter, and Jellyfin then installs the highest
version number, so the .NET 10 build has to sort above the .NET 9 one. On 10.11 the
12.0 entry is filtered out by ABI and only `2.0.0.0` remains.

## Releasing

```
git tag v2.0.0
git push origin v2.0.0
```

`.github/workflows/release.yml` builds both targets, attaches the zips to the GitHub
release, and commits the regenerated `manifest.json` back to `main`. Subscribers see
the update on their next catalog refresh.

The manifest is committed only after the assets upload successfully — its checksums
refer to those exact archives, so it must never advertise a download that is not
there. Note that the archives are not reproducible byte-for-byte (they embed a build
timestamp), so publishing a hand-built zip alongside a CI-built manifest will fail
checksum verification. Let one run produce both.

## Uninstalling

Remove the plugin from the dashboard and restart. The managed CSS block is not
removed automatically on uninstall — disable the plugin (or clear the logo) and save
first if you want it gone, or delete the block by hand in
**Dashboard → General → Custom CSS**.
