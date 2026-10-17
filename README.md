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
