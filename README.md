<p align="center">
  <img src="assets/branding/icon/png/realm-portal-256.png" alt="Realm Portal logo: a bronze coin split in two halves, joined by a red wax key" width="128" height="128">
</p>

<h1 align="center">Realm Portal</h1>

<p align="center"><em>Switch between your World of Warcraft private servers with one click.</em></p>

## What it is

If you play on more than one World of Warcraft private server, changing between them usually means editing game files by hand. Realm Portal is a desktop app that aims to do that for you: you add the servers you play on, pick one, and the game is pointed at it.

It is made for players, not programmers. You bring your own game client and your own servers. The app is available in English and Spanish.

<!-- TODO: add a screenshot of the main window here once the app has one.
     There is no app screenshot in the repository yet. -->

## Status: early development

Realm Portal is **not ready for everyday use yet**.

- The desktop window is still being built, so there is nothing to click yet.
- There are no downloads yet.
- The command-line tool works today for managing your server list (see [Using it today](#using-it-today)). It does not edit your game files yet.

## Install

**Coming soon.** Linux first (AppImage and `.deb`), Windows later. This page will link to the downloads when they exist.

## Using it today

Only the command-line tool can be used right now, and it has to be built from source first (see [CONTRIBUTING.md](CONTRIBUTING.md#build-and-test)). With it you can:

1. **Add a server you play on:** `realm-portal add "My server" logon.example.org`
2. **See your servers:** `realm-portal list`. Add `--client-dir "/path/to/your/game"` to show only the servers that match your game version.
3. **Pick the active one:** `realm-portal switch "My server"`
4. **Check what is selected:** `realm-portal status`

You can also start the game with `realm-portal launch "My server" --client-dir "/path/to/your/game"`.

The planned graphical app will do these same steps with buttons.

## Undoing a change

`realm-portal undo` goes back to the server that was selected before your last switch (or to "no server selected" if there was none). It remembers one step only.

A safer way to change the game's own files is already built into the engine: before any file is changed, a backup is made, and everything can be restored exactly as it was. It is not connected to the app or the command-line tool yet. See [docs/client-files-backup.md](docs/client-files-backup.md) for the details.

## Good to know

- **Not affiliated with Blizzard.** Realm Portal is not affiliated with or endorsed by Blizzard Entertainment.
- **No game downloads.** It never downloads or distributes game clients or game content. You use your own copy of the game.
- **No server list included.** You add the servers you want to play on.
- **Private servers only, for now.**

## License

MIT. See [LICENSE](LICENSE). Licenses of third-party components (packages, textures, fonts) are in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

## For developers

Build instructions, tests, the command-line reference and the project layout are in [CONTRIBUTING.md](CONTRIBUTING.md).
