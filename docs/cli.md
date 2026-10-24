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

Write the command first, then its options: `realm-portal <command> [arguments] [options]`. Options placed before the command (for example `realm-portal --config-dir /tmp/rp list`) are not applied: the tool prints the usage text and exits with code `1`. The only exception is `--verbose`, which works anywhere.

There is no `--help` option: `realm-portal --help` reports an unknown option. To see the usage text, run `realm-portal` with no arguments.

```text
realm-portal --version
realm-portal client-version <client-directory>
realm-portal add <name> <host> [--realm <realm>] [--line <realmlist line>]... [--build <n>] [--account <name>]
realm-portal remove <server>
realm-portal edit <server> [--name <name>] [--host <host>] [--realm <realm>] [--build <n>] [--account <name>] [--client <client|none>]
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
| `client-version <dir>` | Prints the game version and build of the client in `<dir>`. |
| `add` | Adds a server. `<host>` is `host` or `host:port`. `--line` adds an extra `realmlist.wtf` line (repeatable, e.g. `--line "set patchlist patch.example.org"`). `--build` is optional: the client build the server requires (a positive integer). Without it, the server works with any client. |
| `remove` | Removes a server. |
| `edit` | Changes a server. Only the options you pass change. `--client none` unlinks the server from its client. |
| `list` | Lists servers. The active one is marked with `*`. With `--client-dir`, only servers compatible with that client are shown, unless you add `--all`; incompatible ones are marked `[incompatible]`. |
| `switch` | Makes a server the active one. With `--client-dir`, it also backs up and rewrites that client's realm files (`realmlist.wtf` where the client uses it, and `WTF/Config.wtf`). Without `--client-dir`, it only updates Realm Portal's own state and `activo.json`. `--force` skips the compatibility check. |
| `undo` | Restores the server that was active before the last switch. It remembers one step. If that switch used `--client-dir`, it also restores the client's realm files from the backup; otherwise client files are left as they are. |
| `status` | Shows the active server, whether `activo.json` is in sync, clients whose files do not match the active server, and whether undo is available. With `--client-dir`, also shows that client's version and whether it is compatible. |
| `launch` | Needs `--client-dir` (or `REALM_PORTAL_CLIENT_DIR`). With `<server>`, switches to it and starts the game; without it, starts the game for the active server. `--force` skips the compatibility check. `--dry-run` prints what would be launched without switching or starting the game; it still registers the `--client-dir` folder if it is new, and with `<server>` it does not check compatibility. `--wrapper <command>` runs the game through a command such as `wine` and saves it as the launcher prefix in Realm Portal's settings (with `--dry-run` it is only printed, not saved). |
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

On Windows, `~` means your user profile folder (for example `C:\Users\<you>`).

## Options

Options go after the command (see [Commands](#commands)).

| Option | Meaning |
| --- | --- |
| `--config-dir <dir>` | Realm Portal's config folder (`servers.json`, `state.json`, `clients.json`). Overrides `REALM_PORTAL_CONFIG_DIR`. |
| `--projection-dir <dir>` | Folder where `activo.json` is written. Overrides `REALM_PORTAL_PROJECTION_DIR`. |
| `--client-dir <dir>` | Game client folder. Overrides `REALM_PORTAL_CLIENT_DIR`. Only used by `list`, `switch`, `status` and `launch`: registers the client if needed and enables the compatibility filter. |
| `--verbose` | Prints detailed error messages (paths, parse errors). The only option that also works before the command. |

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
| `1` | Usage error (unknown command or option, wrong arguments, `--build` not a positive integer). |
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
