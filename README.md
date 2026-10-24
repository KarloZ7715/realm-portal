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

<p align="center"><img src="docs/screenshots/servers.png" alt="Realm Portal Servers screen with four example servers (My Server, Another Server, Classic Server and Roleplay Realm), each with a Play button" width="800"></p>

## What it does

Realm Portal is a desktop application for World of Warcraft private servers. It registers the servers you play on (name, address, realm, account and required client build), scans your computer for game clients, and points the selected client at the server you choose.

When you switch, Realm Portal writes the connection settings into the client's managed realm files (`realmlist.wtf` where the client version uses it, and `WTF/Config.wtf`), and it takes a backup of those files first. It can then open the game for you. The interface is available in English and Spanish.

## Compatibility and requirements

- **Game clients** (by file layout, see [docs/client-files.md](docs/client-files.md)): 1.12.1 (5875), 2.4.3 (8606), 3.3.5a (12340), 4.3.4 (15595), 5.4.8 (18414), 6.x (WoD) and 7.x (Legion). Other versions fall outside this support.
- **Operating systems**: Linux x64 and Windows x64.
- **Linux runtime**: an X11 server (or XWayland) and `libfontconfig1`, `libfreetype6`, `libx11-6`, `libice6`, `libsm6`.
- The published Linux and Windows builds are self-contained: they do not need a separate .NET runtime.

## Install

The steps below install version 1.0.0. If the [releases page](https://github.com/KarloZ7715/realm-portal/releases/latest) lists a newer version, download that one and replace `1.0.0` with its version number in every command.

### Linux: Debian, Ubuntu, Linux Mint and similar (`.deb`)

1. Download [`realm-portal_1.0.0_amd64.deb`](https://github.com/KarloZ7715/realm-portal/releases/download/v1.0.0/realm-portal_1.0.0_amd64.deb).
2. Open a terminal and run (this assumes the file is in your `Downloads` folder):

   ```bash
   cd ~/Downloads
   sudo apt install ./realm-portal_1.0.0_amd64.deb
   ```

3. Open **Realm Portal** from your applications menu, or run `realm-portal` in a terminal.

To uninstall it, run `sudo apt remove realm-portal`. Your servers, settings and backups stay in your home folder.

### Linux: any distribution (AppImage)

Download [`RealmPortal-1.0.0-x86_64.AppImage`](https://github.com/KarloZ7715/realm-portal/releases/download/v1.0.0/RealmPortal-1.0.0-x86_64.AppImage), then make it executable and run it:

```bash
cd ~/Downloads
chmod +x RealmPortal-1.0.0-x86_64.AppImage
./RealmPortal-1.0.0-x86_64.AppImage
```

### Windows

Download [`RealmPortal-1.0.0-win-x64.zip`](https://github.com/KarloZ7715/realm-portal/releases/download/v1.0.0/RealmPortal-1.0.0-win-x64.zip), unzip it and run `RealmPortal.App.exe`.

## Using the desktop app

1. On first launch, Realm Portal scans standard locations (Steam, Lutris, Wine and Bottles prefixes, and common games folders) and registers the clients and servers it finds.
2. In **Clients**, confirm the detected installations and mark as **In use** the ones you can launch. Use **Search again** for a new scan, or **Pick a folder by hand** to add an installation manually.
3. In **Servers**, select **Add** and enter the server name and address (`host` or `host:port`), and optionally the realm, the account and the target client.
4. Select a server and choose **Play** to switch the client files and open the game, or **Switch only** to change the files without launching. Servers incompatible with the selected client are disabled.
5. After a switch, the Undo notice lets you return the client to the previous server. Client files are backed up before every change (see [Backups and recovery](#backups-and-recovery)).

## Backups and recovery

Realm Portal writes a backup before it changes any client file, so every switch can be undone byte for byte.

- **Automatic**: each switch stores the previous contents of the managed realm files in the backup folder (`~/.local/share/realm-portal/backups` by default) and keeps the 10 most recent backups per client.
- **Undo**: the Undo notice in the app restores the server that was active before the last switch. It remembers one step. If the client files changed by hand since the switch, Realm Portal asks before overwriting them.
- **Manual**: in **Settings → Backup**, use **Manage backups…** to create a backup, list the saved ones, restore an older one or delete one. Backups are stored under the folder set in **Backup folder**.

## Scope and legal notices

- Realm Portal is **not affiliated with or endorsed by Blizzard Entertainment**.
- It does **not** download or distribute game clients or game content; you use your own copy of the game.
- It includes **no server list**; you add the servers you want to play on.
- It supports **private servers only**.
- It writes only the client's managed realm files (`realmlist.wtf` where the client version uses it, and `WTF/Config.wtf`); other game files are left untouched.

## Build and development

Prefer the terminal? The command-line tool is documented in [docs/cli.md](docs/cli.md). Build instructions, tests and the project layout are in [CONTRIBUTING.md](CONTRIBUTING.md).

## License and credits

MIT. See [LICENSE](LICENSE). Licenses of third-party packages are in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

The app icon ("Tesera"), the UI kit, seals and icons are original artwork, MIT-licensed (see [assets/branding/LICENSE-ART.md](assets/branding/LICENSE-ART.md)). Textures are from [ambientCG](https://ambientcg.com) under CC0 1.0, and the bundled typefaces are from [Google Fonts](https://github.com/google/fonts) under the SIL Open Font License 1.1. No Blizzard assets are used.
