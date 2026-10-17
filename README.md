# Realm Portal

Desktop launcher for World of Warcraft private servers. You add your own servers and your own game client: Realm Portal **does not download or distribute game clients or game content**. Linux first, then Windows. Built with C# / .NET 9 and Avalonia, with a "Parchment and bronze" visual style. The user interface is available in Spanish and English.

> Not affiliated with or endorsed by Blizzard Entertainment.

## Status

Phase 3a (in progress): visual layer "Parchment and bronze" (tokens, fonts, themed controls, component gallery).

## Layout

```
src/RealmPortal.Core     core library
src/RealmPortal.Cli      command-line tool
src/RealmPortal.App      Avalonia desktop app
tests/RealmPortal.Tests  xunit tests (core)
tests/RealmPortal.App.Tests  headless Avalonia tests and screenshots (UI)
assets/                  textures, frames, icons, fonts (see assets/ASSETS.md)
```

## Command line

`realm-portal` manages servers without the UI (all output is in English; exit codes: 0 ok, 1 usage, 2 failure):

```bash
realm-portal add Wotlk wotlk.example.org --build 12340 --line "set patchlist patch.example.org"
realm-portal list --client-dir /path/to/client     # only servers this client can play on (--all shows every one)
realm-portal switch Wotlk --client-dir /path/to/client
realm-portal undo
realm-portal status --client-dir /path/to/client
realm-portal launch --client-dir /path/to/client --dry-run
```

Where files go: `--config-dir` / `REALM_PORTAL_CONFIG_DIR` (default `<app data>/realm-portal`: `servers.json`, `state.json`),
`--projection-dir` / `REALM_PORTAL_PROJECTION_DIR` (default `~/.config/wow-servidores`: `activo.json`) and
`--client-dir` / `REALM_PORTAL_CLIENT_DIR`. Always point them at temporary folders when testing.

## Component gallery (development)

Every themed control in every state, rendered with the real theme:

```bash
dotnet run --project src/RealmPortal.App -- --gallery
```

Headless screenshots of the gallery at render scales 1.0, 1.5 and 2.0 (plus seam checks at 1.25 and 1.75) come from the UI tests:

```bash
RP_SCREENSHOT_DIR=/tmp/rp-shots dotnet test tests/RealmPortal.App.Tests -c Release --filter GalleryScreenshotTests
```

The theme draws the designer's UI kit (`assets/ui`, inventory in the kit's `ASSETS.md`); what is still missing is listed in `assets/ART-GAPS.md`.

## Build

Requires the .NET 9 SDK (see `global.json`).

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/RealmPortal.App
```

On Linux the app needs an X11 server (or XWayland) and `libfontconfig1`, `libfreetype6`, `libx11-6`, `libice6`, `libsm6`.

## License

Code is released under the MIT license (see `LICENSE`). Third-party licenses (NuGet packages, textures, fonts): `THIRD_PARTY_LICENSES.md`. Asset inventory: `assets/ASSETS.md`.
