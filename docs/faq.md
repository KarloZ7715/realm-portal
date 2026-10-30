# FAQ and troubleshooting

English · [Español](faq.es.md)

Answers to the questions that come up most often. Each problem lists the likely cause and what to do. Button and menu names are the ones shown in the English interface.

If your problem is not here, see [Reporting a bug](#reporting-a-bug).

## Contents

- [The game still connects to the old server](#the-game-still-connects-to-the-old-server)
- [Realm Portal does not find my game client](#realm-portal-does-not-find-my-game-client)
- [Starting the game through Wine or another wrapper](#starting-the-game-through-wine-or-another-wrapper)
- ["No permission to write" or a read-only `realmlist.wtf`](#no-permission-to-write-or-a-read-only-realmlistwtf)
- [Undoing a switch](#undoing-a-switch)
- [Checking a download with `SHA256SUMS`](#checking-a-download-with-sha256sums)
- [Where Realm Portal keeps its files](#where-realm-portal-keeps-its-files)
- [What Realm Portal does not do](#what-realm-portal-does-not-do)
- [Reporting a bug](#reporting-a-bug)

## The game still connects to the old server

**Wrong client folder.** Realm Portal only changes the client it switched. If you have more than one copy of the game, or you start the game from a shortcut or another launcher, you may be opening a different folder. Check the folder in **Clients**, and use **Play** in Realm Portal so the same client is switched and started.

**The server address was edited after the switch.** Editing the active server does not rewrite the client files by itself, and the server card shows no warning. Choose **Play** on that server to write the new address.

**Old data from the previous server.** If you connect to the right server but see wrong item, quest or creature names, the client's cache is from the previous server. In **Settings → Client cache**, turn on **Clear Cache/WDB when switching servers** (Realm Portal 1.1.0 and later). It is off by default. When it is on, Realm Portal deletes only the `Cache` and `WDB` folders inside the selected client, after a successful switch. To set it for one server only, use **Clear Cache/WDB on switch** in **Edit server…** (in the menu next to **Play**).

**The game was open.** Realm Portal does not switch while the game is running and shows **The game is open**. Close the game and switch again.

## Realm Portal does not find my game client

**The client is not in a place the scan looks.** The scan checks Wine and Bottles prefixes, Lutris games, Steam libraries, `~/Games` and `~/Juegos`, `/opt/games` on Linux, and on Windows the `Program Files` folders, a `Games` folder at the root of each fixed drive, and the registry. The full list is in [What `scan` searches](cli.md#what-scan-searches). It looks only a few folder levels deep and skips hidden folders and symbolic links.

To add a client the scan missed, open **Clients**, choose **Pick a folder by hand** and select the folder that contains `Wow.exe`. **Search again** runs a new scan.

**The folder has no `Wow.exe`.** Realm Portal identifies a client by the `Wow.exe` file directly inside the folder. Pick the folder that holds it, not a parent or a subfolder. If it is missing, the app shows **Game executable not found**.

**The client version is not supported.** Supported clients are 1.12.1 (5875), 2.4.3 (8606), 3.3.5a (12340), 4.3.4 (15595), 5.4.8 (18414), 6.x (WoD) and 7.x (Legion). Other versions show **Unsupported client**.

**The client is found but servers are greyed out.** A server can require a client build. Servers that do not match the selected client are disabled. In **Edit server…**, set **Compatible clients** to **Any client** if the server works with every version. Also check that the client is marked **In use** in **Clients**.

## Starting the game through Wine or another wrapper

By default Realm Portal starts `Wow.exe` directly. That works on Windows. On Linux you need to tell it how to run the game, in **Settings → Launcher**:

- **Command template**: the command that starts the game. `{exe}` is replaced with the path of `Wow.exe` and is required. `{dir}` is replaced with the client folder. Example: `wine {exe}`.
- **Prefix**: a wrapper that goes before everything else, for example `gamemoderun`.
- **Arguments**: extra arguments for the game, for example `-windowed`.
- **Environment variables**: one per line, as `KEY=value`. To use a specific Wine prefix, add `WINEPREFIX=/home/user/.wine-wow` (the folder that contains `drive_c`). Write the full path; `~` is not expanded here.

With these settings, **Play** runs the prefix, then the template (or `Wow.exe`), then the arguments. Nothing is passed through a shell, so `$VAR`, `;` and similar are not interpreted.

If you see **Could not start the game**, check that the command in the template or prefix is installed and in your `PATH` (for example, `command -v wine` in a terminal prints its path). **Invalid launcher settings** means the template is missing `{exe}` or has an unclosed quote, or an environment line is not in the `KEY=value` form.

## "No permission to write" or a read-only `realmlist.wtf`

A read-only `realmlist.wtf` is not a problem on its own. Realm Portal replaces it and keeps it read-only, and **Undo** restores the original file with its original permissions.

**No permission to write** means the operating system does not let your user change the client folder. Common causes:

- On Linux, the client was copied or installed as another user (for example with `sudo`), so that user owns the folder.
- On Windows, the client is under `C:\Program Files`, which needs administrator rights to change.

The simplest fix is to put a copy of the client in a folder you own, such as `~/Games` on Linux or `C:\Games` on Windows (both are scanned), then choose **Search again** in **Clients**. Realm Portal writes only `realmlist.wtf` and `WTF/Config.wtf`, so it needs write access to the folders that hold those files.

**Read-only folder** means the client is on a file system mounted read-only, such as a read-only disk or network share. Move the client to a writable disk.

## Undoing a switch

Before a switch changes the client's `realmlist.wtf` or `WTF/Config.wtf`, Realm Portal backs up the files it is about to change. A switch that changes nothing makes no backup.

- **Last switch**: choose **Undo** in the notice that appears after a switch, or **Undo last switch** at the top of **Servers**. That button stays available after you close and reopen the app, until there is nothing left to undo. Undo goes back one step only.
- **Older backups**: in **Settings → Backup**, choose **Manage backups…**, then **Restore** on the backup you want. By default, Realm Portal keeps the 10 most recent backups per client.
- **Undo stopped**: the client files were changed by hand after the switch. Put them back, or choose **Discard** to drop the pending undo and leave the files as they are.

## Checking a download with `SHA256SUMS`

Every release includes a `SHA256SUMS` file. Download it into the same folder as the package and run:

```bash
cd ~/Downloads
sha256sum --ignore-missing -c SHA256SUMS
```

The line for your file must end in `OK`. On Windows, in PowerShell:

```powershell
Get-FileHash .\RealmPortal-1.1.0-win-x64.zip -Algorithm SHA256
```

Compare the result with the line for that file in `SHA256SUMS`. This check catches a broken or incomplete download. It does not prove who published the file.

## Where Realm Portal keeps its files

| What | Linux | Windows |
| --- | --- | --- |
| Servers, clients and settings | `~/.config/realm-portal` (or `$XDG_CONFIG_HOME/realm-portal`) | `%APPDATA%\realm-portal` |
| Backups of client files | `~/.local/share/realm-portal/backups`, or the folder set in **Settings → Backup** | `%LOCALAPPDATA%\realm-portal\backups`, or the folder set in **Settings → Backup** |
| Error log (1.1.0 and later) | `~/.local/state/realm-portal/logs/realm-portal.log` (or under `$XDG_STATE_HOME`) | `%LOCALAPPDATA%\RealmPortal\logs\realm-portal.log` |
| `activo.json` | `~/.config/wow-servidores` | `%USERPROFILE%\.config\wow-servidores` |

Uninstalling the app does not delete these folders. In the game client, Realm Portal writes only `realmlist.wtf` (where the client version uses it) and `WTF/Config.wtf`. If you turned on cache clearing, it also deletes the client's `Cache` and `WDB` folders.

## What Realm Portal does not do

- It does not download or include game clients. You need your own copy of the game.
- It has no list of servers. You add the servers you play on.
- It is for private servers only, and it is not affiliated with Blizzard Entertainment.
- The release packages contain the desktop app only. The command-line tool runs from source (see [docs/cli.md](cli.md)).

## Reporting a bug

Open a [bug report](https://github.com/KarloZ7715/realm-portal/issues/new?template=bug_report.yml). Include:

- the Realm Portal version (in the status bar, or in **Settings → About Realm Portal…**);
- your operating system and how you installed the app (`.deb`, AppImage, Windows zip or from source);
- the game client version, if the problem involves a client;
- the exact error message, and a screenshot for display problems.

If the app crashed or an action failed without a clear message, attach the relevant lines from the error log listed in [Where Realm Portal keeps its files](#where-realm-portal-keeps-its-files). The log already replaces your home folder with `~`, but read it before posting and remove account names, server addresses or anything else you do not want public.

Security problems should not go in a public issue. See [SECURITY.md](../SECURITY.md).
