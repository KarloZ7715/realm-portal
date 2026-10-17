# Application service (F4)

`RealmPortal.Core.Service.IPortalService` is the **only** thing the UI calls. The UI holds no logic: it passes ids, shows data, and maps
error codes to localized text. This page is the contract; the implementation arrives in a follow-up PR and may refine details only after
review.

## Shape

- **Synchronous.** Everything underneath (applier, stores, PE reader, process probe) is synchronous file/process work, so the service is
  too and the UI wraps calls in `Task.Run`. No `async` ceremony around blocking IO.
- **Results, not exceptions.** Every operation returns `ServiceResult<T>` (`Value` or `Error`); only null arguments throw.
- **Progress.** `SwitchServer` and `Play` take an optional `IProgress<ServiceProgress>`. Callbacks run on the worker thread (the UI marshals).
- **No user-facing text** in Core. An error is a stable `ServiceErrorCode` plus parameters (`ServerName`, `Path`, `LineNumber`,
  `RealmlistKind`, `Field`, `RequiredBuild`, `ClientBuild`, `Consistency`); `Diagnostic` is English text for logs only.

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
ServiceResult<UndoOutcome>                Undo();

ServiceResult<PortalStatus>               GetStatus();
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

**There is no `Account` step.** The applier never writes the account name (`Config.wtf` `accountName` is explicitly left alone, and `ClientFilesRequest` has
no account). `Server.Account` is only a remembered label. See "Open questions".

## Errors

`ServiceErrorCode` mapping (by source):

| Source | Codes |
|---|---|
| `ClientFilesException.Code` | mapped **by name**: `GameRunning`, `PermissionDenied`, `ReadOnlyFs`, `PathNotFound`, `HashMismatch`, `VerifyFailed`, `UnsupportedClient`, `InvalidRealmlist`, `NothingToUndo`; also `ProcessCheckFailed` once the applier raises it. A test fails if the applier gets a code the service lacks. |
| `InvalidRealmlistException` | `InvalidRealmlist` with `RealmlistKind`, `LineNumber`, `ServerName` |
| `ClientVersionException.Kind` | `ClientExecutableNotFound`, `ClientNotAPortableExecutable`, `ClientNoVersionResource`, `ClientUnreadable` (a missing folder: `PathNotFound`), with `Path` |
| `StoreException` | `StoreCorrupt`, `ServerNameTaken`, `ServerNotFound`, `UndoTargetMissing` |
| `ArgumentException` from `Server.Create` | `InvalidServerInput` with `Field` |
| `GameLaunchSpec.Resolve` / settings | `InvalidLauncherSettings` |
| process start failure | `LaunchFailed` |
| incompatible build without `Force` | `IncompatibleClient` with `RequiredBuild`, `ClientBuild` |
| anything else | `Unexpected` (with `Diagnostic`) |

`Consistency` tells the UI what a failed multi-step call left behind: `Unchanged`, `RolledBack` (steps ran and were put back) or `Inconsistent`
(`InconsistentState`: even the rollback failed; tell the user to review the client).

## Consistency between the two undo stacks

Two stacks must move together: the applier's backups (per installation) and `ActiveServerManager`'s one-level undo in `state.json`. The service
keeps a tiny journal of the **last switch** (`last-switch.json` in the config folder: client id and the backup id, or none when the plan was empty)
so undo knows whether the switch changed any client file. One level only, like the manager.

- **Switch:** plan, apply, then activate. If *activate* fails after *apply* succeeded, the service runs `applier.Undo` (the backup it just made) and
  returns the activation error with `RolledBack`; if that rollback fails or reports conflicts the error is `InconsistentState` / `Inconsistent`.
  If plan or apply fails nothing changed (the applier reverts itself): `Unchanged`. The journal is written only after both succeed.
- **Undo:** pre-flight first, changing nothing: a journal entry exists, the previous server still exists and its realmlist is valid, the game is not
  running. Then `applier.Undo` (skipped when the switch made no backup), then `ActiveServerManager.Undo`. If the second step fails after the first
  succeeded, the service re-applies the active server's files and reports `RolledBack`; if that fails, `InconsistentState`.
- **Conflicts:** when `applier.Undo` reports conflicts the app state is **not** touched, the undo stays pending, and the call succeeds with
  `UndoOutcome.HasConflicts == true` and the per-file results, so the UI can show the conflict dialog.
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
3. **Undo conflicts: discard vs restore anyway.** `IClientFilesApplier.Undo` only reports conflicts and leaves the files; it has no "discard the backup" or
   "restore anyway (with a safety backup)" mode. The service exposes `HasConflicts` only; the choice needs two new applier operations first.
4. **`ProcessCheckFailed`.** `IGameProcessProbe.IsGameRunning` returns `bool`, so a failure to list processes cannot surface yet. The code exists in the
   service; it needs the probe/applier change.
5. **Client discovery** (Lutris, Wine, Steam scanning) is not part of this service yet: clients are folders the user adds.
