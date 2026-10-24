<p align="center">
  <img src="assets/branding/icon/png/realm-portal-256.png" alt="Realm Portal icon: a bronze coin split in two halves, joined by a red wax key" width="128" height="128">
</p>

<h1 align="center">Realm Portal</h1>

<p align="center"><em>Manage your World of Warcraft private servers and switch the game client between them.</em></p>

<p align="center">English · <a href="README.es.md">Español</a></p>

<p align="center">
  <a href="https://github.com/KarloZ7715/realm-portal/actions/workflows/ci.yml"><img src="https://github.com/KarloZ7715/realm-portal/actions/workflows/ci.yml/badge.svg" alt="CI status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"></a>
</p>

## What it does

Realm Portal is a desktop application for World of Warcraft private servers. It registers the servers you play on (name, address, realm, account and required client build), scans your computer for game clients, and points the selected client at the server you choose.

When you switch, Realm Portal writes the connection settings into the client's managed realm files (`realmlist.wtf` where the client version uses it, and `WTF/Config.wtf`), and it takes a backup of those files first. The desktop app runs the switch and launches the game; the CLI exposes the same server operations for scripting and headless use. The interface is available in English and Spanish.

## Compatibility and requirements

- **Game clients** (by file layout, see [docs/client-files.md](docs/client-files.md)): 1.12.1 (5875), 2.4.3 (8606), 3.3.5a (12340), 4.3.4 (15595), 5.4.8 (18414), 6.x (WoD) and 7.x (Legion). Other versions fall outside this support.
- **Operating systems**: Linux x64 and Windows x64.
- **Linux runtime**: an X11 server (or XWayland) and `libfontconfig1`, `libfreetype6`, `libx11-6`, `libice6`, `libsm6`.
- The published Linux and Windows builds are self-contained: they do not need a separate .NET runtime.

## Install

Download the build for your platform from [Releases](https://github.com/KarloZ7715/realm-portal/releases):

- **Linux — `.deb`**: install the package with your distribution's package manager.
- **Linux — `.AppImage`**: make it executable and run it.

```bash
chmod +x RealmPortal-*-x86_64.AppImage
./RealmPortal-*-x86_64.AppImage
```

- **Windows — `.zip`**: unzip it and run `RealmPortal.App.exe`.

## Using the desktop app

1. On first launch, Realm Portal scans standard locations (Steam, Lutris, Wine and Bottles prefixes, and common games folders) and registers the clients and servers it finds.
2. In **Clients**, confirm the detected installations and mark as **In use** the ones you can launch. Use **Search again** for a new scan, or **Pick a folder by hand** to add an installation manually.
3. In **Servers**, select **Add** and enter the server name and address (`host` or `host:port`), and optionally the realm, the account and the target client.
4. Select a server and choose **Play** to switch the client files and open the game, or **Switch only** to change the files without launching. Servers incompatible with the selected client are disabled.
5. After a switch, the Undo notice lets you return the client to the previous server. Client files are backed up before every change (see [Backups and recovery](#backups-and-recovery)).

## Using the command line

`realm-portal` manages servers without the app. The commands are `add`, `remove`, `edit`, `list`, `switch`, `undo`, `status`, `launch`, `client` (`list`, `add`, `edit`, `remove`), `scan` and `client-version <dir>`; `--version` prints the version.

```bash
realm-portal add "Example" logon.example.org --realm Icecrown --build 12340
realm-portal list --client-dir "/path/to/client"
realm-portal switch "Example" --client-dir "/path/to/client"
realm-portal launch "Example" --client-dir "/path/to/client"
realm-portal undo
realm-portal status --client-dir "/path/to/client"
realm-portal client list
realm-portal scan --root /games
```

Common options: `--config-dir`, `--projection-dir` and `--client-dir` point the tool at its config folder, the `activo.json` folder and the game client. `--all` lists every server, `--force` skips the compatibility check, `--dry-run` prints the launch command instead of starting the game, and `--wrapper <command>` runs the game through a command such as `wine`. With `--client-dir`, `switch` also edits the client files; without it, it only updates Realm Portal's own state. `--verbose` prints detailed error messages; exit codes are `0` (success), `1` (usage) and `2` (failure).

## Backups and recovery

Realm Portal writes a backup before it changes any client file, so every switch can be undone byte for byte.

- **Automatic**: each switch stores the previous contents of the managed realm files in the backup folder (`~/.local/share/realm-portal/backups` by default) and keeps the 10 most recent backups per client.
- **Undo**: `realm-portal undo`, or the Undo notice in the app, restores the server that was active before the last switch. It remembers one step. If the client files changed by hand since the switch, Realm Portal asks before overwriting them.
- **Manual**: in **Settings → Backup**, use **Manage backups…** to create a backup, list the saved ones, restore an older one or delete one. Backups are stored under the folder set in **Backup folder**.

The backup folder and the other locations can be overridden with `REALM_PORTAL_BACKUP_DIR`, `REALM_PORTAL_CONFIG_DIR` and `REALM_PORTAL_PROJECTION_DIR`.

## Scope and legal notices

- Realm Portal is **not affiliated with or endorsed by Blizzard Entertainment**.
- It does **not** download or distribute game clients or game content; you use your own copy of the game.
- It includes **no server list**; you add the servers you want to play on.
- It supports **private servers only**.
- It writes only the client's managed realm files (`realmlist.wtf` where the client version uses it, and `WTF/Config.wtf`); other game files are left untouched.

## Build and development

Build instructions, tests, the CLI reference and the project layout are in [CONTRIBUTING.md](CONTRIBUTING.md).

## License and credits

MIT. See [LICENSE](LICENSE). Licenses of third-party packages are in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

The app icon ("Tesera"), the UI kit, seals and icons are original artwork, MIT-licensed (see [assets/branding/LICENSE-ART.md](assets/branding/LICENSE-ART.md)). Textures are from [ambientCG](https://ambientcg.com) under CC0 1.0, and the bundled typefaces are from [Google Fonts](https://github.com/google/fonts) under the SIL Open Font License 1.1. No Blizzard assets are used.
