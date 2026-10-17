# Application service (F4)

`RealmPortal.Core.Service.IPortalService` is the **only** thing the UI calls. The UI holds no logic: it passes ids, shows data, and maps
error codes to localized text. This page is the contract; the implementation arrives in a follow-up PR and may refine details only after
review.

## Shape

- **Synchronous.** Everything underneath (applier, stores, PE reader, process probe) is synchronous file/process work, so the service is
  too and the UI wraps calls in `Task.Run`. No `async` ceremony around blocking IO.
- **Results, not exceptions.** Every operation returns `ServiceResult<T>` (`Value` or `Error`; `Succeeded` is annotated so `Value`/`Error` are non-null without `!`); only null arguments throw. `IProgress.Report` is never called after the method returned. Every call is safe from a thread-pool thread.
- **Progress.** `SwitchServer` and `Play` take an optional `IProgress<ServiceProgress>`. Callbacks run on the worker thread (the UI marshals).
- **No user-facing text** in Core. An error is a stable `ServiceErrorCode` plus parameters (`ServerName`, `Path`, `LineNumber`,
  `RealmlistKind`, `Field`, `RequiredBuild`, `ClientBuild`, `Consistency`); `ClientVersion`, `ConflictingFiles`, `Diagnostic` is English text for logs only.

```csharp
ServiceResult<IReadOnlyList<ClientEntry>> ListClients();             // unreadable folder = entry with Error, not a failure
ServiceResult<ClientEntry>                AddClient(string folder);
ServiceResult<Unit>                       RemoveClient(string clientId);
ServiceResult<bool>                       IsGameRunning(string clientId);

ServiceResult<IReadOnlyList<ServerEntry>> ListServers(string? clientId = null, bool compatibleOnly = false);
ServiceResult<ServerEntry>                AddServer(NewServer server);
ServiceResult<Unit>                       RemoveServer(string serverId);

ServiceResult<SwitchOutcome>              SwitchServer(SwitchRequest request, IProgress<ServiceProgress>? progress = null);
ServiceResult<SwitchOutcome>              Play(SwitchRequest request, IProgress<ServiceProgress>? progress = null);
ServiceResult<UndoOutcome>                Undo();                // single level; UndoConflict (+ ConflictingFiles) when files changed since
ServiceResult<PortalStatus>               DiscardUndo();         // gives up the pending undo; client files untouched; next state NothingToUndo

ServiceResult<PortalStatus>               GetStatus();
ServiceResult<StorageSettings>            GetStorageSettings();  // backup folder
ServiceResult<Unit>                       SetStorageSettings(StorageSettings settings);
ServiceResult<LauncherSettings>           GetLauncherSettings();
ServiceResult<Unit>                       SetLauncherSettings(LauncherSettings settings);
```

Ids: `ClientEntry.Id` is derived from the folder path (`c` + 16 hex digits of the SHA-256 of the full path, lowercase on Windows), so it is stable and is
also the `installationId` of the backups. The server id is the `profileId`. Both satisfy `ClientFilesRequest.IsValidId`.

## Steps and progress

| `ServiceStep` | What runs | Used by |
|---|---|---|
| `Plan` | `applier.Plan(new ClientFilesRequest(installationId, profileId, clientRoot, clientVersion, server.Realmlist, server.Realm))` (dry run) | switch, play |
| `WriteFiles` | `applier.Apply(plan)`: backup first, then `realmlist.wtf` / `Config.wtf`. **One indivisible call**, so "Backup" and "Realmlist" cannot be reported separately | switch, play |
| `Activate` | `ActiveServerManager.Switch` (`state.json` + `activo.json`) | switch, play |
| `Launch` | `IGameLaunchService.Launch(GameLaunchSpec)` | play |

Each step emits `Started`, then `Completed`, or `Failed` with the `ServiceError`. After a failure no later step starts. A plan with no edits (the client
already has this configuration) still reports `WriteFiles` Started/Completed with `SwitchOutcome.BackupId == null`.

**Re-switching the server that is already active** for the chosen client is valid (not an error). The service still runs `Plan`. When the plan has edits
(client files drifted), it applies them as a repair. When the plan is empty, it still reports `WriteFiles` Started/Completed with `BackupId == null`. In both
cases it does **not** replace the pending single-level undo or the `last-switch.json` journal with a no-op entry: a no-op re-switch must not erase the undo
from the previous real switch to a different server. `ActiveServerManager.Switch` is still called (projection refresh; undo in `state.json` is unchanged when
the server was already active). In `SwitchOutcome`, `Previous` equals `Active` (the same server). When the re-switch repairs drifted files, the applier may
create a new backup, but the journal's `backup_id` is **not** replaced (undo still restores the bytes from the original switch).

**There is no `Account` step.** The applier never writes the account name (`Config.wtf` `accountName` is explicitly left alone, and `ClientFilesRequest` has
no account). `Server.Account` is only a remembered label. See "Open questions".

## Errors

`ServiceErrorCode` mapping (by source):

| Source | Codes |
|---|---|
| `ClientFilesException.Code` | mapped **by name**: `GameRunning`, `PermissionDenied`, `ReadOnlyFs`, `PathNotFound`, `HashMismatch`, `VerifyFailed`, `UnsupportedClient` (with `ClientVersion`), `InvalidRealmlist`, `NothingToUndo`; also `ProcessCheckFailed` once the applier raises it (the service already returns it from `IsGameRunning` when the probe throws). A test fails if the applier gets a code the service lacks. |
| `InvalidRealmlistException` | `InvalidRealmlist` with `RealmlistKind`, `LineNumber`, `ServerName` |
| `ClientVersionException.Kind` | `ClientExecutableNotFound`, `ClientNotAPortableExecutable`, `ClientNoVersionResource`, `ClientUnreadable` (a missing folder: `PathNotFound`), with `Path` |
| `StoreException` | `StoreCorrupt`, `ServerNameTaken`, `ServerNotFound`, `NothingToUndo`, `UndoTargetMissing` |
| undo finds changed client files | `UndoConflict` with `ConflictingFiles`, `Consistency.Pending` |
| backup folder cannot be used | `PathNotFound`, `PermissionDenied`, `ReadOnlyFs` |
| `ArgumentException` from `Server.Create` | `InvalidServerInput` with `Field` |
| `GameLaunchSpec.Resolve` / settings | `InvalidLauncherSettings` |
| process start failure | `LaunchFailed` |
| incompatible build without `Force` | `IncompatibleClient` with `RequiredBuild`, `ClientBuild` |
| anything else | `Unexpected` (with `Diagnostic`) |

`Consistency` tells the UI what a failed multi-step call left behind: `Unchanged`, `RolledBack` (steps ran and were put back), `Inconsistent`
(`InconsistentState`: even the rollback failed; tell the user to review the client) or `Pending` (only `UndoConflict`: stopped on purpose, can be retried or discarded).

## Consistency between the two undo stacks

Two stacks must move together: the applier's backups (per installation) and `ActiveServerManager`'s one-level undo in `state.json`. The service
keeps a tiny journal of the **last switch** (`last-switch.json` in the config folder: optional `client_id` and optional `backup_id`)
so undo knows whether **that** switch changed client files. One level only, like the manager. Every successful switch writes the journal, including
CLI switches without `--client-dir` (`client_id` and `backup_id` both omitted). A later state-only switch **replaces** the journal so undo never
replays a backup from an earlier `--client-dir` switch. Re-switching the **same** server to repair drifted files creates a new backup in the applier
store but **does not** replace the journal's `backup_id` (undo still restores the original switch's bytes).

- **Switch:** plan, apply, then activate. If *activate* fails after *apply* succeeded, the service runs `applier.Undo` (the backup it just made) and
  returns the activation error with `RolledBack`; if that rollback fails or reports conflicts the error is `InconsistentState` / `Inconsistent`.
  If plan or apply fails nothing changed (the applier reverts itself): `Unchanged`. The journal is written only after both succeed.
- **Undo:** pre-flight first, changing nothing: `state.json` has a pending undo, the previous server still exists and its realmlist is valid, the game is not
  running when client files would be touched. Then `applier.Undo` for the journal's `backup_id` only when that id is set (skipped for state-only switches),
  then `ActiveServerManager.Undo`. Undo **never** writes client files for state-only switches (`client_id` omitted in the journal): registered clients
  may still drift from `activo.json` until the user runs `switch <server> --client-dir <folder>`. The CLI prints whether client files were updated.
  If the second step fails after file undo succeeded, the service re-applies the active server's
  files and reports `RolledBack`; if that fails, `InconsistentState`.
- **Conflicts:** when `applier.Undo` reports conflicts the app state is **not** touched and the undo stays pending. The call fails with `UndoConflict`,
  `ConflictingFiles` (the files that differ from what the switch wrote, never overwritten) and `Consistency.Pending`. Note the applier restores the files that
  did not conflict before it reports the others, so the client may be partially restored; `Undo()` again after the user fixed the files finishes the job.
- **Discard:** `DiscardUndo()` gives up the pending undo without touching client files or the active server. The next state is `NothingToUndo` (`CanUndo == false`):
  undo is single-level, there is no "next pending backup". Restore-anyway is **not** in the contract: it will be added only if the applier gets such an operation
  (never a silent forced restore).
- **Backup folder:** `PortalStatus.BackupFolder` is where backups go right now; `GetStorageSettings`/`SetStorageSettings` read and write the configured folder (validated:
  `PathNotFound`/`PermissionDenied`/`ReadOnlyFs`). A change applies from the next start and existing backups stay where they are. The UI language is **not** persisted
  by the service; the UI keeps its own file.
- **Client of a server:** `NewServer.ClientId` / `ServerEntry.ClientId` is the preferred client chosen when the server was added (optional). It is only a label for the UI;
  `SwitchRequest` still names the client.
- **Play** = switch + launch. A failed launch leaves the switch done (the client is configured, the server is active, undo still works) and returns
  `LaunchFailed`.

## Launcher settings

`LauncherSettings(CommandTemplate, Arguments, Environment, Prefix)` is a `GameLaunchSpec` without the executable and working directory; the service
completes it with the selected client's `Wow.exe` and folder (`ToSpec`). Persisted in the config folder; `SetLauncherSettings` validates by resolving it
(`InvalidLauncherSettings`). Nothing goes through a shell (see [launch.md](launch.md)).

## Open questions

1. **Account step.** Not in the applier. If the product wants the account name pre-filled in the login screen, the applier needs a request field and a
   write rule first; until then the UI should show only Plan/WriteFiles/Activate(/Launch). For the demo labels: Backup+Realmlist → `WriteFiles`.
2. **Finer progress.** `Apply` is one call; separate "Backup" and "Realmlist" events would need an `IProgress` in `IClientFilesApplier.Apply` (Systems).
3. **Discard and restore-anyway in the applier.** `IClientFilesApplier.Undo` only reports conflicts and leaves them; it has no "discard the backup" or "restore anyway
   (with a safety backup)" operation. `DiscardUndo()` is therefore implemented in the service (it forgets the pending undo; the applier's backup folder stays until its
   retention). Restore-anyway needs the applier first. Also: the applier restores non-conflicting files before reporting a conflict; detecting all conflicts first would make
   `UndoConflict` mean "nothing was touched".
4. **`ProcessCheckFailed`.** `IGameProcessProbe.IsGameRunning` returns `bool`, so a failure to list processes cannot surface yet. The code exists in the
   service; it needs the probe/applier change.
5. **Client origin.** `ClientEntry` has no origin (manual/Lutris/Steam) and there is no rescan yet; it will need an `Origin` field when discovery arrives.
6. **Game exit.** The UI polls `IsGameRunning(clientId)` after `Play`; an exit callback is a later nicety.
7. **Client discovery** (Lutris, Wine, Steam scanning) is not part of this service yet: clients are folders the user adds.
