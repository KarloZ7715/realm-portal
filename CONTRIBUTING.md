# Contributing to Realm Portal

Thanks for your interest! This project is MIT-licensed; the app UI is available in Spanish and English.

## Project layout

```text
src/RealmPortal.Core         core library
src/RealmPortal.Cli          command-line tool (executable name: realm-portal)
src/RealmPortal.App          Avalonia desktop app
tests/RealmPortal.Tests      xunit tests (core and CLI)
tests/RealmPortal.App.Tests  headless Avalonia tests and screenshots (UI)
assets/                      textures, frames, icons, fonts (see assets/ASSETS.md)
docs/                        design notes for the client-file engine and game launch
```

Built with C# / .NET 9 and Avalonia, in a "Parchment and bronze" visual style.

## Build and test

Requires the .NET SDK pinned in [`global.json`](global.json) (9.0.x).

```bash
dotnet restore RealmPortal.sln
dotnet build RealmPortal.sln -c Release --no-restore
dotnet test RealmPortal.sln -c Release --no-build
dotnet run --project src/RealmPortal.App
```

On Linux the app needs an X11 server (or XWayland) and `libfontconfig1`, `libfreetype6`, `libx11-6`, `libice6`, `libsm6`.

CI runs build + tests on Ubuntu 22.04 and Windows, plus a secret scan, a per-file size check and (on pull requests) a commit author email check.

## Version

The product version lives in one place: `<Version>` in [`Directory.Build.props`](Directory.Build.props). `AppInfo.Version`
reads the assembly informational version (without the `+<commit>` suffix), so a source build shows it in the status bar and in
`realm-portal --version`. The release workflow resolves its package version from the same file with
`packaging/resolve-version.sh` and passes it to `dotnet publish` (`-p:Version`), so release builds use the same value; a `v<version>`
tag that does not match `Directory.Build.props` fails the release run. Bump `<Version>` in a PR before tagging a release.
`AppInfoTests` checks that the app and CLI report the props version.

## Command line

`realm-portal` manages servers without the UI. Exit codes: 0 ok, 1 usage, 2 failure.
Run it from source with `dotnet run --project src/RealmPortal.Cli -- <command>`.

```bash
realm-portal add Wotlk wotlk.example.org --build 12340 --line "set patchlist patch.example.org"
realm-portal list --client-dir /path/to/client     # only servers this client can play on (--all shows every one)
realm-portal switch Wotlk --client-dir /path/to/client   # --force switches even if the client is incompatible
realm-portal undo
realm-portal status --client-dir /path/to/client
realm-portal launch --client-dir /path/to/client --dry-run
realm-portal launch Wotlk --client-dir /path/to/client --wrapper wine   # switch first, then start through a wrapper command
```

Options: `--force` (on `switch` and `launch`) skips the compatibility check; `--wrapper <command>` (on `launch`) runs the game
through a command such as `wine` or `gamemoderun` (it becomes the prefix of the launch command); `--dry-run` (on `launch`) prints the
launch command and does not start the client, but if you pass a server name it still switches to that server first.
`--build` must be a positive integer (anything else is a usage error, exit 1).

What the commands write: `add`, `remove`, `switch`, `undo`, and `launch <server>` (including with `--dry-run`) only change Realm Portal's own files (`servers.json`, `state.json`,
`activo.json`). **They do not edit the game client yet**: `ClientFilesApplier` (backup, write `realmlist.wtf`/`Config.wtf`, byte-identical undo,
see [docs/client-files-backup.md](docs/client-files-backup.md)) exists in `RealmPortal.Core` but the CLI does not call it, so `launch` starts
the client with whatever realm configuration it already has. Connecting both is planned in the application service for the desktop app.

Where files go: `--config-dir` / `REALM_PORTAL_CONFIG_DIR` (default `<app data>/realm-portal`: `servers.json`, `state.json`),
`--projection-dir` / `REALM_PORTAL_PROJECTION_DIR` (default `~/.config/wow-servidores`: `activo.json`) and
`--client-dir` / `REALM_PORTAL_CLIENT_DIR`. Always point them at temporary folders when testing.

## Component gallery

Every themed control in every state, rendered with the real theme:

```bash
dotnet run --project src/RealmPortal.App -- --gallery
```

Headless screenshots of the gallery at render scales 1.0, 1.5 and 2.0 (plus seam checks at 1.25 and 1.75) come from the UI tests:

```bash
RP_SCREENSHOT_DIR=/tmp/rp-shots dotnet test tests/RealmPortal.App.Tests -c Release --filter GalleryScreenshotTests
```

The theme draws the designer's UI kit (`assets/ui`, inventory in the kit's `ASSETS.md`); what is still missing is listed in `assets/ART-GAPS.md`.

## Screens (simulated data)

`dotnet run --project src/RealmPortal.App` opens the compact UI (servers, clients, add server) with **simulated data only**: the lists come from
`Demo/DemoData.cs`, switching and launching are timed steps in `Demo/DemoBackend.cs`, and nothing reads or writes a game client, `~/.config/wow-servidores`
or the network. Screens, states and the assumptions behind them are in [docs/ui-screens.md](docs/ui-screens.md).
UI strings live in `src/RealmPortal.App/Localization/Strings/{es,en}.json` (a test checks both files have the same keys and that every key used in XAML or code exists);
use `{loc:T some.key}` in XAML and `Loc.Get` / `Loc.Format` in code.

Screenshots of every screen and state at render scales 1.0, 1.5 and 2.0:

```bash
RP_SCREENSHOT_DIR=/tmp/rp-shots dotnet test tests/RealmPortal.App.Tests -c Release
```

## Design notes

- [docs/client-files.md](docs/client-files.md): what is written into the game client.
- [docs/client-files-backup.md](docs/client-files-backup.md): backup, apply and undo.
- [docs/launch.md](docs/launch.md): starting the game.
- [docs/ui-screens.md](docs/ui-screens.md): the F3b screens, their states and what is simulated.
- [assets/ASSETS.md](assets/ASSETS.md) and [assets/branding/README.md](assets/branding/README.md): assets and the Tésera mark.

## Secret scanning (gitleaks)

Never commit credentials, tokens, private keys or `.env` files. CI scans the full git
history with [gitleaks](https://github.com/gitleaks/gitleaks); you can catch leaks earlier
by enabling the pre-commit hook:

```bash
pip install pre-commit      # or: brew install pre-commit
pre-commit install          # scans staged changes on every commit
pre-commit run --all-files  # optional: scan on demand
```

If a secret was committed, treat it as compromised: revoke/rotate it first, then remove it
from history. A confirmed false positive can be allow-listed in a `.gitleaks.toml`
(explain why in the PR).

## File size limit

Keep the repository small: no tracked file may exceed **5 MiB**. The `file-size` CI job
(`.github/workflows/file-size.yml`) checks every tracked file and fails the build otherwise.
Today the largest asset is under 1.5 MiB, so the limit leaves plenty of room for textures
and fonts.

If a file genuinely has to be larger (for example a texture or font that cannot be
reduced), list its exact repository-relative path in
[`.github/file-size-allowlist.txt`](.github/file-size-allowlist.txt) with a comment explaining
why, and mention it in the PR description. Prefer Git LFS or an external download for
anything bigger than a few MiB. Large raw design sources and build outputs do not belong
in the repository.

## Commit author email

Commits must be authored with your GitHub noreply address (`<id>+<username>@users.noreply.github.com`,
see <https://github.com/settings/emails>), never a personal or institutional email. On pull requests the `commit-email`
CI job (`.github/workflows/commit-email.yml`) fails if any new commit has another author email (a small, documented
allowlist covers bots such as Cursor agents); fix it with `git config user.email ...` and `git commit --amend --reset-author`
or a rebase on the PR branch.

## Reporting vulnerabilities

Please do not open public issues for security problems. See [SECURITY.md](SECURITY.md).
