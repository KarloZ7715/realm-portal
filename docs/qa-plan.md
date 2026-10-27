# QA test plan

Scope: what a player must be able to do with Realm Portal, and how each step is checked. The plan covers what exists on `main` today
(Core, CLI, `ClientFilesApplier`, the application service that wires UI/CLI to the engine, and the Avalonia UI).
Results are not committed: `tests/qa/run_all.sh` writes one `PASS | FAIL | KNOWN-FAIL` line per scenario to
`$QA_OUT/qa-cli-results.txt` and `$QA_OUT/qa-engine-results.txt` (`QA_OUT` defaults to `/tmp`).

## Safety rules

- **Never** use a real game client, `~/.config/wow-servidores` or the real `realm-portal` config. Every run happens in a fresh `mktemp -d` folder with a **synthetic client**
  (a tiny PE file named `Wow.exe` whose version resource says e.g. `3.3.5.12340`, plus fake `realmlist.wtf` / `WTF/Config.wtf`).
- The scripts redirect `HOME`, `--config-dir`, `--projection-dir`, `--client-dir` and `REALM_PORTAL_BACKUP_DIR` into that folder, and the applier probe refuses any path outside the system temp folder.
- No test starts a real game: `launch` is checked with `--dry-run` or with a recording wrapper script.

## How the tests are run

| Kind | What | Command |
|---|---|---|
| **Unit** | xunit tests of Core, CLI and UI view models | `dotnet test RealmPortal.sln -c Release` |
| **CLI** | `realm-portal` against synthetic clients and temp config | `tests/qa/cli_scenarios.sh` |
| **Engine** | the real `ClientFilesApplier` (plan/apply/undo) against synthetic clients, driven by `tests/qa/ApplierProbe` | `tests/qa/client_files_scenarios.sh` |
| **Manual UI** | steps in the window (UI is wired to the real application service) | see the table |

Helpers: `tests/qa/lib.sh` (asserts, temp dirs), `tests/qa/make_fake_wow_exe.py` (synthetic `Wow.exe`), `tests/qa/ApplierProbe` (not part of the solution).
Requirements: .NET 9 SDK, `bash`, `python3`, `jq`, `sha256sum`, `od`. Run `tests/qa/run_all.sh` to execute everything. Set `QA_KEEP=1` to keep the temp folder.

## Status legend

- **Now**: automatable today (CLI, engine or unit level).
- **Partial**: the engine or CLI part runs now; an end-to-end or platform-specific part is Manual UI or not yet automated.
- **Done (UI wired)**: the UI path exists via the application service (`IPortalService`); automated coverage may still be CLI/engine only.
- **Manual UI**: verified by hand in the window (headless Avalonia tests cover view models / simulated cases).

## Scenarios

| ID | Scenario | Preconditions | Steps | Expected result | How it is run | Status |
|---|---|---|---|---|---|---|
| QA-01 | Clone and build | Clean machine with .NET 9 SDK | `git clone`, `dotnet restore`, `dotnet build RealmPortal.sln -c Release`, `dotnet test` | Build with 0 warnings/errors; all tests pass | Unit (CI also runs it) | Now |
| QA-02 | Point to a client folder | Synthetic client folders: valid 3.3.5a; no `Wow.exe`; `Wow.exe` that is not a PE; lowercase `wow.exe`; missing folder; unreadable folder | `client-version <dir>`; `list --client-dir <dir>` | Version `12340` is read; every bad case exits 2 with a one-line message (no stack trace) | CLI `QA-02*`, `QA-11f/g`; Unit `PeClientVersionReaderTests` | Now. UI folder picker: Done (UI wired) |
| QA-03 | See only compatible servers | Servers requiring 12340, 8606 and "any" | `list --client-dir <3.3.5a>`; `--all`; `switch <incompatible>`; `--force` | Only 12340 + "any" shown; `--all` tags the rest `[incompatible]`; incompatible switch refused (exit 2, nothing written) unless `--force` | CLI `QA-03*`; Unit `CompatibilityFilterTests` | Now. UI badge/disabled Play: Done (UI wired) |
| QA-04 | One-click switch | Server added, client chosen | `switch <name>`; `status` | `activo.json` written, `status` shows "in sync"; name lookup is case-insensitive | CLI `QA-04*` | Partial: the client files (`realmlist.wtf`, `Config.wtf`) are written only by the engine (QA-07b); the CLI does not call it yet |
| QA-04b | Switch writes the game's files | 3.3.5a client with two locale folders | Plan, apply the new realmlist | Every `Data/<locale>/realmlist.wtf` and `WTF/Config.wtf` updated, other lines and files untouched; other client layouts (1.12.1, 5.4.8) follow `docs/client-files.md`; unsupported versions refused | Engine `CF-01`, `CF-06`, `CF-08` | Now (engine). UI one-click: Done (UI wired) |
| QA-05 | Open the game | Client with `Wow.exe` | `launch --dry-run`; `launch --wrapper <recorder>`; no `Wow.exe`; no `--client-dir`; unknown server | Dry run names the executable and server; the wrapper receives the full path (spaces kept as one argument); errors exit 2 (or 1 for usage) | CLI `QA-05*`; Unit `GameLaunchSpecTests` | Now. UI "Play" step list and "game running" banner: Done (UI wired) |
| QA-06 | Reopen the app and stay on that server | A server is active | Run `status` in a new process | Same server active, `activo.json` in sync, `list` marks it with `*` | CLI `QA-06*`; Unit `ActiveServerManagerTests` | Now. UI restart: Done (UI wired) |
| QA-07a | Undo is byte-identical (`activo.json`) | Fresh config/projection/backup/HOME for each sub-case (no journal left by earlier groups); server A active, B added | `switch B`; `undo`; compare `sha256sum activo.json`. Stale journal (QA-07a7): fake client, `switch A --client-dir <c>`, `switch B`, `switch A`, `undo` | Hash after undo equals hash before the switch; undo of the first-ever switch removes the file; second undo exits 2. QA-07a7: undo exits 0, active server is B, `activo.json` matches B, and (QA-07a7c) the client's `realmlist.wtf` is not written by the undo: it still holds A's realmlist from `switch A --client-dir` (byte-identical), neither B's realmlist nor the original `orig.example.org`, so the old client-files journal is not replayed and state-only switches write no client (AC changed 2026-10-17, see below). **QA-07a7c requires the client-files path of PR #58** (`switch --client-dir` writes and journals client files); the script detects it at run time (client file written, backup `manifest.json` or `last-switch.json` present after the first switch) and records QA-07a7c as KNOWN "requires client-files path (PR #58)" on a CLI without it (e.g. `main`) | CLI `QA-07a*` | Now (QA-07a7c: with PR #58) |
| QA-07b | Undo is byte-identical (client files) | Client with existing files (CRLF, LF, BOM, duplicates, empty, random bytes) and a client missing `Config.wtf` | Apply, then undo; compare a hash of every file in the client tree | Tree identical to before; created files are deleted; edited files restored; second undo says `NothingToUndo` | Engine `CF-01`, `CF-02`, `CF-06`, `CF-08`; Unit `ClientFilesApplierTests` | Now (engine). Undo button: Done (UI wired) |
| QA-08 | Add a server by hand, invalid realmlist | Empty store | `add` with: space, non-ASCII, quote, duplicate key, `portal`, uppercase `SET`, free text, empty value/host/name, duplicate name, bad `--build`, `host:port` | Invalid input exits 2 (1 for bad `--build`), nothing is stored; valid `host:port` accepted | CLI `QA-08*`; Unit `RealmlistTextTests`; Engine `CF-03` | Now. UI add form validation text: Done (UI wired) |
| QA-09 | Game running | A process named `Wow` or `Wow.exe` exists | Apply, then undo | Both refused with `GameRunning`, client untouched; work once the process ends | Engine `CF-04` (a copy of `sleep` named `Wow`); Unit `ClientFilesApplierTests` | Now (engine). Banner in the UI: Done (UI wired) |
| QA-10 | Corrupt config | Corrupt/empty/BOM/`null`/wrong-version `servers.json`; corrupt `state.json`; hand-edited invalid realmlist; corrupt `Config.wtf`; truncated `manifest.json` | Run `list`, `add`, `switch`, `status`; engine apply/undo | Exit 2 with a message naming the file; the corrupt file is never overwritten; no silent reset | CLI `QA-10*`; Engine `CF-06` | Now |
| QA-11 | Permissions | Read-only projection/config/backup/client folders; unreadable files; read-only `realmlist.wtf`; symlinked `realmlist.wtf` | Run the operation | Clear error (exit 2 / `PermissionDenied`), no stack trace, everything already written is reverted; read-only files and symlinks are respected | CLI `QA-11*`; Engine `CF-05h-j`, `CF-07*` | Now |
| QA-12 | `activo.json` compatible with the prototype | Any active server (with realm and `patchlist`) | Inspect the file bytes | Valid JSON; keys `server_id, name, host, realm, realmlist` in that order; UTF-8 without BOM; `realmlist` is ASCII, starts with lowercase `set realmlist `, uses `\r\n`; file ends with one `\n`; no temp files left; `status` detects tampering | CLI `QA-12*`; Unit `ActiveProjectionTests`, `ServerRealmlistContractTests` | Now |
| QA-13 | Remove servers and odd names | Servers, one active | `remove` active/non-active; same switch twice; non-ASCII name | Removing the active server deletes `activo.json`; one undo level kept; ASCII-only `realmlist` field even for a non-ASCII name | CLI `QA-13*` | Now |
| QA-14 | Concurrency | Two processes at once (CLI + CLI, later CLI + UI) | Parallel `add` and `switch` | No lost server, `activo.json` stays in sync with `state.json` | CLI `QA-14*` | Now |
| QA-15 | Backup conflict | Applied change; user edits `realmlist.wtf` afterwards | `undo` | Conflicting file reported and left untouched; other files restored; next undo reports the same conflict; tampered/truncated backup refused; unusable backup root fails before touching the client | Engine `CF-05*` | Now (engine). Conflict dialog in the UI: Done (UI wired) |
| QA-16 | Linux launch of a Windows client | Linux machine, `Wow.exe` | `launch` without and with `--wrapper wine` | Clear message when the executable cannot start | CLI `QA-05*` (wrapper only; real Wine is not run) | Partial; real Wine run: Manual UI |
| QA-17 | UI end to end | App wired to the real service | Pick folder, see only compatible servers, one-click switch, Play, restart, undo, add server with a bad address, game running, conflict, permissions | Same results as the CLI/engine rows, with localized (ES/EN) messages | Manual UI (headless Avalonia tests cover view models / simulated cases) | Manual UI |

### Acceptance criteria changes

- **2026-10-17, QA-07a7c:** following Seguridad's review of PR #58, undo writes client files only for the `client_id`/`backup_id` stored in the last switch's journal (`last-switch.json`), and a switch without `--client-dir` journals no client, so undoing it writes to no client (see `docs/app-service.md`, "Consistency between the two undo stacks"). QA-07a7c previously expected the client's `realmlist.wtf` to match B after undo; it now expects the client to be untouched (still A's realmlist). It still fails if undo reverts the file to the original.

## Out of scope for now

Real game launch, real servers or network tests, Windows runs (CI covers build and unit tests on Windows), installers and packaging.
