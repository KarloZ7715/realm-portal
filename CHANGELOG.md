# Changelog

<!-- markdownlint-configure-file { "MD024": { "siblings_only": true } } -->

All notable changes to Realm Portal are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0](https://github.com/KarloZ7715/realm-portal/compare/v1.0.1...v1.1.0) - 2026-10-28

### Added

- First-run welcome screen: choose the language, review the detected clients and the imported servers ([#180](https://github.com/KarloZ7715/realm-portal/issues/180))
- About screen with the version, credits, license and a link to the project ([#179](https://github.com/KarloZ7715/realm-portal/issues/179))
- Optional clearing of the client's `Cache` and `WDB` folders when switching servers, off by default, with a per-server override ([#181](https://github.com/KarloZ7715/realm-portal/issues/181))
- Server login status dot, which can be turned off in Settings ([#183](https://github.com/KarloZ7715/realm-portal/issues/183), [#184](https://github.com/KarloZ7715/realm-portal/issues/184))
- New-version notice with a daily check and a **Check for updates** button, which can be turned off in Settings ([#185](https://github.com/KarloZ7715/realm-portal/issues/185), [#186](https://github.com/KarloZ7715/realm-portal/issues/186))
- Local error log for crashes and failed background actions ([#178](https://github.com/KarloZ7715/realm-portal/issues/178))
- The server address field and the CLI accept pasted `set realmlist` lines ([#177](https://github.com/KarloZ7715/realm-portal/issues/177))

### Changed

- The game watcher checks less often once the game process is seen ([#174](https://github.com/KarloZ7715/realm-portal/issues/174))
- The `.deb` package ships a machine-readable `copyright` file ([#175](https://github.com/KarloZ7715/realm-portal/issues/175))
- The release pipeline supports prerelease (`rc`) tags, which are never offered as the latest version ([#188](https://github.com/KarloZ7715/realm-portal/issues/188))
- Privacy section in the README for the two network features ([#182](https://github.com/KarloZ7715/realm-portal/issues/182))
- Install links and file names point to the 1.0.1 release ([#152](https://github.com/KarloZ7715/realm-portal/issues/152))
- Docs use feature names instead of internal milestone codes ([#187](https://github.com/KarloZ7715/realm-portal/issues/187))

### Fixed

- A restored window keeps its title bar and borders on screen, and a damaged saved window position no longer drops the other preferences ([#172](https://github.com/KarloZ7715/realm-portal/issues/172))
- CLI: `undo --client-dir` undoes that client's last switch ([#176](https://github.com/KarloZ7715/realm-portal/issues/176))
- CLI: the undo notice prints its lines instead of an escaped newline ([#153](https://github.com/KarloZ7715/realm-portal/issues/153))
- CLI: control characters in error messages are escaped, so they cannot add lines to the terminal output ([#167](https://github.com/KarloZ7715/realm-portal/issues/167))
- CLI: `client-version` names the option it rejects and honors `--help` ([#171](https://github.com/KarloZ7715/realm-portal/issues/171))

### Security

- Switching refuses managed files or folders that are symbolic links pointing outside the client folder ([#173](https://github.com/KarloZ7715/realm-portal/issues/173))

## [1.0.1](https://github.com/KarloZ7715/realm-portal/compare/v1.0.0...v1.0.1) - 2026-10-24

### Added

- The window remembers its size, position and maximized state ([#120](https://github.com/KarloZ7715/realm-portal/issues/120))
- A server can be set to work with any client ([#137](https://github.com/KarloZ7715/realm-portal/issues/137))

### Changed

- The release packages include the license texts of every bundled third-party component, including the .NET runtime ([#126](https://github.com/KarloZ7715/realm-portal/issues/126), [#138](https://github.com/KarloZ7715/realm-portal/issues/138))
- README install steps, a current screenshot and a CLI reference in `docs/cli.md` ([#110](https://github.com/KarloZ7715/realm-portal/issues/110), [#128](https://github.com/KarloZ7715/realm-portal/issues/128))
- Personal example paths and names replaced with neutral placeholders ([#109](https://github.com/KarloZ7715/realm-portal/issues/109), [#144](https://github.com/KarloZ7715/realm-portal/issues/144))

### Fixed

- Editing a server keeps its build and patchlist, and the next scan no longer adds a duplicate ([#119](https://github.com/KarloZ7715/realm-portal/issues/119))
- A Wine process that already exited no longer counts as a running game ([#146](https://github.com/KarloZ7715/realm-portal/issues/146))
- Client versions are shown as "3.3.5a (12340)" ([#113](https://github.com/KarloZ7715/realm-portal/issues/113), [#127](https://github.com/KarloZ7715/realm-portal/issues/127))
- Settings asks before discarding unsaved changes ([#118](https://github.com/KarloZ7715/realm-portal/issues/118))
- Imported servers are named after the realm, or the host when there is no realm ([#121](https://github.com/KarloZ7715/realm-portal/issues/121))
- The Clients screen no longer shows a misleading Settings hint ([#111](https://github.com/KarloZ7715/realm-portal/issues/111))
- Back buttons in Backups and Edit client show a single arrow ([#112](https://github.com/KarloZ7715/realm-portal/issues/112))
- Settings launcher fields show example values that do not look like real ones ([#114](https://github.com/KarloZ7715/realm-portal/issues/114))
- The unsupported-client message lists 6.x and 7.x ([#91](https://github.com/KarloZ7715/realm-portal/issues/91))
- Builds from source report the right version ([#115](https://github.com/KarloZ7715/realm-portal/issues/115))
- CLI: `--config-dir`, `--projection-dir` and `--client-dir` work before the command ([#132](https://github.com/KarloZ7715/realm-portal/issues/132))
- CLI: `launch --dry-run` shows the exact command, including the configured wrapper ([#134](https://github.com/KarloZ7715/realm-portal/issues/134))

## [1.0.0](https://github.com/KarloZ7715/realm-portal/releases/tag/v1.0.0) - 2026-10-19

First stable release. Known limitations at release: symbolic links inside the client folder that point outside it were followed for `realmlist.wtf` and `Config.wtf` ([#44](https://github.com/KarloZ7715/realm-portal/issues/44), fixed in 1.1.0), and the app was built on Avalonia 11.3 ([#48](https://github.com/KarloZ7715/realm-portal/issues/48)).

### Added

- Desktop app for Linux x64 and Windows x64, in English and Spanish
- Client scan in Wine and Bottles prefixes, Lutris, Steam and common game folders, plus adding a client folder by hand
- Support for clients 1.12.1, 2.4.3, 3.3.5a, 4.3.4, 5.4.8, 6.x (WoD) and 7.x (Legion)
- Server list with name, address, realm, account and required client build, and import of the servers already set in each client
- Switching a client to a server by writing `realmlist.wtf` and `WTF/Config.wtf`, with a backup taken first
- Undo of the last switch, and a backups screen to create, restore and delete backups (10 kept per client by default)
- Starting the game, with a configurable command template, prefix, arguments and environment variables
- `activo.json` with the active server, for companion tools
- Command-line tool, run from source
- `.deb`, AppImage and Windows `.zip` packages, with a `SHA256SUMS` file
