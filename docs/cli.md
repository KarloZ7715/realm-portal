# Command line (`RealmPortal.Cli`)

Realm Portal has a command-line tool that does the same server work as the desktop app: add and edit servers, switch a game client to a server, undo, check status, scan for clients and launch the game. It is meant for scripting and headless use. Most people only need the desktop app (see the [README](../README.md)).

## Getting the CLI

The release packages (`.deb`, AppImage, Windows `.zip`) ship the **desktop app only**. The `realm-portal` command installed by the `.deb` opens the desktop app, not this tool.

Run the CLI from a clone of the repository (needs the .NET SDK, see [CONTRIBUTING.md](../CONTRIBUTING.md)):

```bash
dotnet run --project src/RealmPortal.Cli -- <command> [options]
```

In the examples below, `realm-portal` stands for `dotnet run --project src/RealmPortal.Cli --` (or for the `realm-portal` executable if you publish `src/RealmPortal.Cli` yourself).

## Commands

Write the command, then its arguments and options: `realm-portal <command> [arguments] [options]`. The common options (`--config-dir`, `--projection-dir`, `--client-dir`) and `--verbose` may also come before the command: `realm-portal --config-dir /tmp/rp list` is the same as `realm-portal list --config-dir /tmp/rp`. When one of these three directory options is given twice, the last one wins. Any other option before the command is reported as an unknown option (exit code `1`).

`realm-portal --help` or `realm-portal -h` prints the version and usage text and exits 0. Running `realm-portal` with no arguments does the same (exit 0).

```text
realm-portal --version
realm-portal --help
realm-portal -h
realm-portal client-version <client-directory>
realm-portal add <name> <host> [--realm <realm>] [--line <realmlist line>]... [--build <n>] [--account <name>]
realm-portal remove <server>
realm-portal edit <server> [--name <name>] [--host <host>] [--realm <realm>] [--build <n|any>] [--account <name>] [--client <client|none>]
realm-portal list [--all]
realm-portal switch <server> [--force]
realm-portal undo
realm-portal status
realm-portal launch [<server>] [--force] [--dry-run] [--wrapper <command>]
realm-portal client list
realm-portal client add <path> [--name <name>]
realm-portal client edit <client> [--name <name>] [--path <path>]
realm-portal client remove <client>
realm-portal scan [--root <folder>]...
```

Running `realm-portal` with no arguments prints the version and this usage text.

- `<server>` is a server id, a unique id prefix or a server name (case-insensitive).
- `<client>` is a client id, a unique id prefix, a client name or its folder path.

| Command | What it does |
| --- | --- |
| `--version` (`-v`) | Prints the version. |
| `client-version <dir>` | Prints the game version and build of the client in `<dir>`. Takes no options (including the common `--config-dir` / `--projection-dir` / `--client-dir`); a rejected option is named on stderr. `--help` / `-h` after `client-version` print usage and exit 0. |
| `add` | Adds a server. `<host>` is `host` or `host:port`. `--line` adds an extra `realmlist.wtf` line (repeatable, e.g. `--line "set patchlist patch.example.org"`). `--build` is optional: the client build the server requires (a positive integer). Without it, the server works with any client. |
| `remove` | Removes a server. |
| `edit` | Changes a server. Only the options you pass change; everything else (including the required build and extra realmlist lines such as `patchlist`) is kept. `--build <n>` sets the required client build; `--build any` clears it, so the server works with any client. `--client none` unlinks the server from its client. |
| `list` | Lists servers. The active one is marked with `*`. With `--client-dir`, only servers compatible with that client are shown, unless you add `--all`; incompatible ones are marked `[incompatible]`. |
| `switch` | Makes a server the active one. With `--client-dir`, it also backs up and rewrites that client's realm files (`realmlist.wtf` where the client uses it, and `WTF/Config.wtf`). Without `--client-dir`, it only updates Realm Portal's own state and `activo.json`. `--force` skips the compatibility check. |
| `undo` | Restores the server that was active before the last switch. It remembers one step. With `--client-dir`, restores that client's last switch (and its realm files from the backup when that switch rewrote them), even if another client has a more recent global switch. Without `--client-dir`, undoes the globally latest switch. `REALM_PORTAL_CLIENT_DIR` alone does not scope undo. |
| `status` | Shows the active server, whether `activo.json` is in sync, clients whose files do not match the active server, and whether undo is available. With `--client-dir`, also shows that client's version and whether it is compatible. |
| `launch` | Needs `--client-dir` (or `REALM_PORTAL_CLIENT_DIR`). With `<server>`, switches to it and starts the game; without it, starts the game for the active server. `--force` skips the compatibility check. `--dry-run` prints the exact command a real launch would start (the saved launcher prefix such as `gamemoderun`, or `--wrapper` when given, then the executable; tokens with spaces are quoted; environment changes, if any, on following `env NAME=value` / `unset NAME` lines) without switching or starting the game, and fails the same way a real launch would when a saved launcher setting cannot be applied by the CLI; it still registers the `--client-dir` folder if it is new, and with `<server>` it does not check compatibility. `--wrapper <command>` runs the game through a command such as `wine` and saves it as the launcher prefix in Realm Portal's settings (with `--dry-run` it is only printed, not saved). |
| `client list` / `add` / `edit` / `remove` | Manages registered game clients. Removing a client leaves the game folder and its backups on disk. |
| `scan` | Searches for game clients and imports the servers found in their realm files. See [What `scan` searches](#what-scan-searches). |

## What `scan` searches

`scan` looks in these places (folders that do not exist are skipped) and in every `--root` folder you add:

- **Wine**: the `$WINEPREFIX` folder if that variable is set, `~/.wine`, every prefix in `~/.local/share/wineprefixes/`, and `~/.local/share/lutris/runners/wine`.
- **Bottles**: the `drive_c` folder of every bottle in `~/.local/share/bottles/bottles/`.
- **Lutris**: the game folders listed in the Lutris game files (`*.yml`) in `~/.config/lutris/games/` and `~/.local/share/lutris/games/`.
- **Game folders**: `~/Games` and `~/Juegos`.
- **Steam**: `steamapps/common` and `steamapps/compatdata` (Proton prefixes) of the Steam installation and its extra libraries. Steam is looked for in `~/.steam/steam`, `~/.steam/root`, `~/.local/share/Steam`, the Flatpak (`~/.var/app/com.valvesoftware.Steam/.local/share/Steam`) and Snap (`~/snap/steam/common/.local/share/Steam`) locations, and on Windows in `Program Files (x86)\Steam`.
- **Linux (and other non-Windows systems)**: `/opt/games`.
- **Windows**:
  - the `Program Files` and `Program Files (x86)` folders;
  - a `Games` folder at the root of every fixed drive (for example `C:\Games`, `D:\Games`);
  - the `InstallPath` and `GamePath` values of the registry key `SOFTWARE\Blizzard Entertainment\World of Warcraft`, read from `HKEY_LOCAL_MACHINE` (32-bit and 64-bit views) and `HKEY_CURRENT_USER`.

In this list, `~` is your home folder (on Windows, your user profile folder, for example `C:\Users\<you>`). Realm Portal does not expand `~` in `--root` values itself: on Linux an unquoted `~` is expanded by the shell, but on Windows pass the full path (for example `--root "C:\Users\<you>\Games"`).

## Options

These options may go before or after the command (see [Commands](#commands)).

| Option | Meaning |
| --- | --- |
| `--config-dir <dir>` | Realm Portal's config folder (`servers.json`, `state.json`, `clients.json`). Overrides `REALM_PORTAL_CONFIG_DIR`. |
| `--projection-dir <dir>` | Folder where `activo.json` is written. Overrides `REALM_PORTAL_PROJECTION_DIR`. |
| `--client-dir <dir>` | Game client folder. Overrides `REALM_PORTAL_CLIENT_DIR`. Used by `list`, `switch`, `status`, `launch`, and `undo`. For `list`/`switch`/`status`/`launch`: registers the client if needed and enables the compatibility filter. For `undo`: scopes restoration to that client's last switch (must already be registered; does not register or read `Wow.exe`). The environment variable alone does not scope `undo`. |
| `--verbose` | Prints detailed error messages (paths, parse errors). |

## Environment variables

| Variable | Default (Linux) | Default (Windows) |
| --- | --- | --- |
| `REALM_PORTAL_CONFIG_DIR` | `$XDG_CONFIG_HOME/realm-portal` or `~/.config/realm-portal` | `%APPDATA%\realm-portal` |
| `REALM_PORTAL_PROJECTION_DIR` | `~/.config/wow-servidores` | `%USERPROFILE%\.config\wow-servidores` |
| `REALM_PORTAL_BACKUP_DIR` | `~/.local/share/realm-portal/backups` | `%LOCALAPPDATA%\realm-portal\backups` |
| `REALM_PORTAL_CLIENT_DIR` | none | none |

The config, projection and backup variables are also read by the desktop app. When testing, point them at temporary folders.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | Usage error (unknown command or option, wrong arguments, a `--build` value that is neither a positive integer nor, on `edit`, `any`). |
| `2` | The operation failed (for example, no matching server, an incompatible client, or a file error). |

## Examples

```bash
realm-portal add "My Server" logon.example.org --realm "Example Realm" --build 12340
realm-portal list --client-dir ~/Games/WoW-335a
realm-portal switch "My Server" --client-dir ~/Games/WoW-335a
realm-portal launch "My Server" --client-dir ~/Games/WoW-335a --wrapper wine
realm-portal launch --client-dir ~/Games/WoW-335a --dry-run
realm-portal undo
realm-portal status --client-dir ~/Games/WoW-335a
realm-portal client list
realm-portal scan --root ~/Games
```
