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

Commits must use your GitHub noreply address, preferably `<id>+<username>@users.noreply.github.com`
(see <https://github.com/settings/emails>), for both the author and the committer, never a personal or
institutional email.

On pull requests the `commit-email` CI job (`.github/workflows/commit-email.yml`, logic in
`.github/scripts/check-commit-emails.sh`) checks the author (`%ae`) and committer (`%ce`) email of every new
commit. An email passes when, ignoring case, the whole address matches
`^([0-9]+\+)?[a-z0-9]([a-z0-9]|-[a-z0-9]){0,38}(\[bot\])?@users\.noreply\.github\.com$`, that is, an
optional numeric ID and a valid GitHub username (1 to 39 letters, digits or single inner hyphens; the script
checks the length separately), optionally
followed by `[bot]` (for example
`188834590+KarloZ7715@users.noreply.github.com`, `KarloZ7715@users.noreply.github.com` or
`49699333+dependabot[bot]@users.noreply.github.com`), or is exactly one of:

- `noreply@github.com`: committer of commits created on github.com (web merges and edits) and by GitHub apps;
- `cursoragent@cursor.com`: Cursor cloud agents.

Addresses the regex cannot express, such as Enterprise Managed User (EMU) accounts
(`<id>+<username>_<shortcode>@users.noreply.github.com`) or older accounts whose usernames do not follow the
current rules, can be added as exact addresses to `EXACT_ALLOWED_EMAILS` in
`.github/scripts/check-commit-emails.sh` (explain why in the PR).

Any other author or committer email fails the job. A noreply-domain address whose username is not valid is
reported as such, so the error tells you to fix the username part. `Co-authored-by:` trailers are checked with the same rule
but only produce a warning (a trailer without `<email>` is parsed from its bare address, or reported as
malformed). Fix a failing branch with `git config user.email ...` and
`git commit --amend --no-edit --reset-author` (or a rebase with `--exec`) on the PR branch. When you merge or
edit on github.com, pick your noreply address in the commit dialog. Run
`.github/scripts/test-check-commit-emails.sh` after changing the check.

**This check protects privacy; it does not verify identity.** Author, committer and `Co-authored-by` emails are
free text: anyone can set any allowed address, including someone else's noreply address or
`cursoragent@cursor.com`, and the job will pass. Its only purpose is to stop personal or institutional addresses
from being published by mistake. To prove who made a commit, sign your commits (GPG, SSH or S/MIME, see
<https://docs.github.com/en/authentication/managing-commit-signature-verification>) and enable vigilant mode
in your GitHub settings so that unsigned commits attributed to you are flagged as "Unverified".

## Reporting vulnerabilities

Please do not open public issues for security problems. See [SECURITY.md](SECURITY.md).
