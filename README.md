# Realm Portal

**ES** — Lanzador de escritorio para servidores privados de World of Warcraft. Tú añades tus propios servidores y tu propio cliente de juego: Realm Portal **no descarga ni distribuye clientes ni contenido del juego**. Primero Linux, después Windows. Hecho con C# / .NET 9 y Avalonia; estilo visual «Pergamino y bronce».

**EN** — Desktop launcher for World of Warcraft private servers. You add your own servers and your own game client: Realm Portal **does not download or distribute game clients or game content**. Linux first, then Windows. Built with C# / .NET 9 and Avalonia; "Parchment and bronze" visual style.

> Not affiliated with or endorsed by Blizzard Entertainment. / No afiliado ni respaldado por Blizzard Entertainment.

## Estado / Status

Fase 1: esqueleto del proyecto (ventana vacía). / Phase 1: project skeleton (empty window).

## Estructura / Layout

```
src/RealmPortal.Core   biblioteca de lógica / core library
src/RealmPortal.Cli    herramienta de línea de comandos / command-line tool
src/RealmPortal.App    aplicación Avalonia / Avalonia desktop app
tests/RealmPortal.Tests  pruebas xunit / xunit tests
assets/                texturas, marcos, iconos, fuentes (ver assets/ASSETS.md)
```

## Compilar / Build

Requiere el SDK de .NET 9 (ver `global.json`). / Requires the .NET 9 SDK (see `global.json`).

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/RealmPortal.App
```

En Linux la app necesita un servidor X11 (o XWayland) y `libfontconfig1`, `libfreetype6`, `libx11-6`, `libice6`, `libsm6`.

## Licencia / License

Código bajo licencia MIT (ver `LICENSE`). Licencias de terceros (NuGet, texturas, fuentes): `THIRD_PARTY_LICENSES.md`. Inventario de assets: `assets/ASSETS.md`.
