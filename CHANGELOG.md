# Changelog

## v2.1.2 — 2026-09-18

Adds favicon and splash-screen replacement.

### Downloads

| Your Jellyfin | Download | Build |
| --- | --- | --- |
| 10.11.x | `customlogo_2.0.2.0_jf10.11.zip` | 2.0.2.0 (.NET 9) |
| 12.0.x, 12.1.x | `customlogo_2.1.2.0_jf12.0.zip` | 2.1.2.0 (.NET 10) |

### Added

- **Favicon replacement** — the browser tab icon, and the icon used when the site is
  pinned or installed as an app.
- **Splash logo replacement** — the logo shown while the app loads, before the
  interface appears. This is the option removed in 2.1.1; it is now implemented by a
  mechanism that can actually reach it.
- A status line on the configuration page reporting whether the patch succeeded, plus
  `GET /CustomLogo/Status`.

### How it works, and what it costs

Neither of these is reachable from branding CSS. The favicon is a `<link>` in the
document head, which CSS cannot address at all; the splash markup is discarded when
React mounts, before the branding stylesheet is injected. The only way to change
either is to edit the served document, so the plugin now patches the web client's
`index.html` and writes the logo alongside it.

This is more invasive than CSS injection, so it is bounded:

- The untouched `index.html` is copied into the plugin's data folder, and every
  generated version is produced from that copy — the transform is idempotent and a
  restore is exact.
- Turning the options off, or clearing the logo, restores the original file and deletes
  the asset.
- A Jellyfin or web-client update replaces `index.html`; the new file will not carry the
  plugin's marker, so it is adopted as the new original and the patch is re-applied on
  the next start.
- The logo file is content-addressed (`customlogo-asset.<hash>.png`) so browsers refetch
  it when it changes. Favicons are cached hard.

**It requires write access to the web client folder.** Docker installs normally have
it. Distribution packages often do not — the web files are typically owned by root
while Jellyfin runs as `jellyfin`. Where the write fails the plugin logs a warning,
reports it on the configuration page, and carries on: the header logo is unaffected,
since that goes through branding CSS and touches no files.

## v2.1.1 — 2026-09-18

Makes the plugin work on the 12.x web interface. Version 2.0.0 only ever affected the
legacy interface.

### Downloads

| Your Jellyfin | Download | Build |
| --- | --- | --- |
| 10.11.x | `customlogo_2.0.1.0_jf10.11.zip` | 2.0.1.0 (.NET 9) |
| 12.0.x, 12.1.x | `customlogo_2.1.1.0_jf12.0.zip` | 2.1.1.0 (.NET 10) |

### Fixed

- **The logo now applies on the 12.x interface.** The modern client renders it as an
  `<img>` (`ServerButton` in the top bar, `DrawerHeaderLink` in the navigation drawer),
  not as a CSS background, so the 2.0.0 rules had no effect there. The image is now
  swapped with `content: url(...)`, with `!important` to override MUI's inline sizing.
  The same two components back the dashboard, so it is covered as well.

### Added

- Separate height settings for the 12.x top bar (default `1.75em`) and navigation
  drawer (default `2.5rem`). Width stays automatic, so a wide logo grows sideways
  instead of being squashed into the square the stock icon occupies.

### Removed

- The splash-screen option. Branding CSS is injected by a React component after mount,
  by which point the splash markup is already gone, so the rule could never apply. It
  was doing nothing in 2.0.0.

### Notes

- 12.1 needs no separate build. Its API surface is identical to 12.0.0 for everything
  the plugin uses, and `targetAbi` is a minimum, so the `12.0.0.0` package covers both.
- A user who enables "Disable custom CSS" in their own display settings will not see
  the custom logo.

## v2.0.0 — 2026-09-18

First working release. The plugin previously did not compile; this is effectively a
rewrite of everything except the plugin GUID, which is unchanged so existing
installs upgrade in place.

### Downloads

| Your Jellyfin | Download | Build |
| --- | --- | --- |
| 10.11.x | `customlogo_2.0.0.0_jf10.11.zip` | 2.0.0.0 (.NET 9) |
| 12.0.x | `customlogo_2.1.0.0_jf12.0.zip` | 2.1.0.0 (.NET 10) |

There is no Jellyfin 10.12. Jellyfin dropped the static leading `10.`, so what
would have been 10.12.0 shipped as **12.0** — use the `jf12.0` build on it.

Installing from the repository picks the right build automatically:

```
https://raw.githubusercontent.com/ScallywagDude/CustomLogoPluginRevamped/main/manifest.json
```

### Added

- Configuration page under Dashboard → Plugins → Custom Logo, with upload, live
  preview, and removal.
- Independent toggles for the header logo and the splash / loading logo.
- Configurable header logo width, validated as a CSS length.
- An "Additional CSS" field, scoped to the block the plugin manages.
- `GET /CustomLogo/Logo` serves the configured image with its real content type.
  Anonymous by design — a branding logo is public, and the sign-in screen needs it
  before credentials exist. The web client does not use it; the CSS carries the
  image inline.

### Fixed

- The plugin now compiles. `BasePlugin<T>` was constructed with one argument
  instead of `(IApplicationPaths, IXmlSerializer)`, and the controller referenced
  `Plugin.Instance` and `Plugin.Instance.ConfigurationManager`, neither of which
  exists.
- The configuration page is now reachable. `IHasWebPages` was never implemented and
  `configPage.html` was not an embedded resource, so the page could not be served.
- The logo is now actually applied. The old controller wrote a PNG to disk and
  stopped there — nothing ever changed the web client.

### Changed

- Retargeted from .NET 6 / Jellyfin 10.8 packages to Jellyfin 10.11.11 and 12.0.0.
- Assembly renamed to `Jellyfin.Plugin.CustomLogo`.
- Branding replacement works by injecting a delimited block into the stylesheet
  Jellyfin serves at `/Branding/Css`. Custom CSS you wrote yourself, before or
  after that block, is preserved; disabling the plugin or clearing the logo removes
  the block and leaves your CSS untouched. The block is re-asserted at every server
  start, so a restored backup cannot leave branding half-applied.
- The image is embedded as a `data:` URI rather than referenced by URL. The login
  and splash screens render before any credentials exist, and a relative URL in
  injected CSS resolves against the current page rather than the stylesheet, which
  breaks behind a reverse-proxy base path.
- `build.js` produces both ABI packages, writes `meta.json` into each, computes the
  MD5 digest Jellyfin uses to verify downloads, and merges the results into
  `manifest.json` rather than overwriting it, so older releases stay available.
- Added `.github/workflows/release.yml`: tagging `v*` builds both targets, attaches
  the zips to the release, and commits the regenerated manifest back to `main`.

### Notes

- Hard-refresh the web client (Ctrl+F5) after saving — browsers cache the branding
  stylesheet.
- Keep the image well under 1 MB. It is inlined into a stylesheet every client
  downloads.
- The managed CSS block is not removed on uninstall. Disable the plugin and save
  first, or delete the block by hand in Dashboard → General → Custom CSS.
- Packages are not reproducible byte-for-byte — `meta.json` carries a build
  timestamp. Publish the zips and the `manifest.json` from the same build, or
  checksum verification will fail at install time.
