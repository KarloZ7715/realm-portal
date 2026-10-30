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

CI runs build + tests on Ubuntu 22.04 and Windows, plus a secret scan and a per-file size check.

## Version

The product version lives in one place: `<Version>` in [`Directory.Build.props`](Directory.Build.props). `AppInfo.Version`
reads the assembly informational version (without the `+<commit>` suffix), so a source build shows it in the status bar and in
`realm-portal --version`. The release workflow resolves its package version from the same file with
`packaging/resolve-version.sh` and passes it to `dotnet publish` (`-p:Version`), so release builds use the same value; a `v<version>`
tag that does not match `Directory.Build.props` fails the release run. Bump `<Version>` in a PR before tagging a release.
`AppInfoTests` checks that the app and CLI report the props version.

## Changelog

[`CHANGELOG.md`](CHANGELOG.md) follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Entries come from the
Conventional Commit subjects of merged PRs: `feat` goes to Added; `perf`, `refactor`, `packaging` and `docs` to Changed; `fix`
to Fixed. `chore`, `ci`, `test`, `build`, `style` and release-pipeline fixes are left out. These rules live in
[`cliff.toml`](cliff.toml) for [git-cliff](https://git-cliff.org) (tested with 2.14.2; install it only if you prepare a release).

In the PR that bumps `<Version>`, add the new section:

```bash
git cliff --unreleased --tag v1.2.0 --prepend CHANGELOG.md
```

Then review it: reword entries for users, move security fixes to Security, add changes whose commit subject is not
conventional, and drop changes users do not see. Older sections are never regenerated. If another PR merges before you tag,
add its entry before tagging, so the section covers everything up to the tagged commit. git-cliff dates the section with
the UTC date of the moment it runs, not the tag date; check the date and change it to the tag's UTC date if they differ.

Use a version's reviewed `CHANGELOG.md` section, generated with git-cliff as above, as the notes of its GitHub release.
Whoever prepares the release copies it there; nothing does it automatically.

To write one version's section to a file:

```bash
awk '/^## \[1\.2\.0\]/{f=1;next} /^## \[/{f=0} f' CHANGELOG.md > /tmp/notes-1.2.0.md
```

## Command line

`src/RealmPortal.Cli` builds the `realm-portal` command-line tool. Run it from source with
`dotnet run --project src/RealmPortal.Cli -- <command>`. Commands, options, environment variables and exit codes are documented in
[docs/cli.md](docs/cli.md). When testing, point `--config-dir`, `--projection-dir` and `--client-dir` at temporary folders, and set `REALM_PORTAL_BACKUP_DIR` to a temporary folder too.

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
- [docs/ui-screens.md](docs/ui-screens.md): UI screens, their states, and what the early prototype simulates.
- [assets/ASSETS.md](assets/ASSETS.md) and [assets/branding/README.md](assets/branding/README.md): assets and the Tésera mark.

## NuGet lock files

This repo enables `RestorePackagesWithLockFile` (see `Directory.Build.props`). CI restores with
`dotnet restore --locked-mode`, so every `packages.lock.json` must match `Directory.Packages.props`.

Dependabot often updates `Directory.Packages.props` and the lock file of the project that
declares a package, but not locks of projects that only take it transitively (for example
`tests/RealmPortal.App.Tests` via `RealmPortal.Tests`). After a Dependabot NuGet PR, or after
any manual bump of a centrally managed package version, refresh every lock file:

```bash
dotnet restore RealmPortal.sln --force-evaluate
dotnet restore RealmPortal.sln --locked-mode   # must succeed before you push
```

Commit any updated `**/packages.lock.json` files with the version bump.

A ready-to-enable workflow lives at
[`.github/workflow-proposals/dependabot-lockfiles.yml`](.github/workflow-proposals/dependabot-lockfiles.yml).
Copy it to `.github/workflows/dependabot-lockfiles.yml` (requires a GitHub credential with the
`workflow` scope) to have Actions regenerate lock files automatically on Dependabot PRs:
`dependabot[bot]` only, `dependabot/**` branches of this repository only (never forks), with
`contents: write` scoped to that job.

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

## Use of AI tools

AI tools are welcome, but you are the author: review every line and be able to explain what your change does and how it
interacts with the rest of the app. State in the pull request whether and how you used AI; the PR template asks for it.

## Reporting vulnerabilities

Please do not open public issues for security problems. See [SECURITY.md](SECURITY.md).
