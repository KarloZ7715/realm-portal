# Application service (F4 / 1.0.0 v2)

`RealmPortal.Core.Service.IPortalService` is the **only** thing the UI calls. The UI holds no logic: it passes ids, shows data, and maps
error codes to localized text. This page is the `1.0.0` (v2) service contract covering client/server discovery (`Scan`), full client and
server CRUD (`UpdateClient`, `UpdateServer`), per-client active state (`active_by_client` in `state.json`) and concurrency invariants,
first-class backup management (`ListBackups`, `CreateBackup`, `RestoreBackup`, `DeleteBackup`), and storage retention settings (`KeepBackups`).

## Shape

- **Synchronous.** Everything underneath (applier, stores, scanner, PE reader, process probe) is synchronous file/process work, so the service is
  too and the UI wraps calls in `Task.Run`. No `async` ceremony around blocking IO.
- **Results, not exceptions.** Every operation returns `ServiceResult<T>` (`Value` or `Error`; `Succeeded` is annotated so `Value`/`Error` are non-null without `!`); only null arguments throw. `IProgress.Report` is never called after the method returned. Every call is safe from a thread-pool thread.
- **Progress.** `SwitchServer` and `Play` take an optional `IProgress<ServiceProgress>`; `Scan` takes an optional `IProgress<ScanProgress>`. The service invokes `IProgress<T>.Report` on the **worker thread** that is running the operation (typically a thread-pool thread when the UI wrapped the call in `Task.Run`). **Where the progress handler runs** depends on the `IProgress` implementation: `Progress<T>` captures `SynchronizationContext.Current` at construction and posts callbacks to that context, so the App should create progress on the UI thread (or dispatch inside a custom `IProgress` implementation). The service does not marshal to the UI thread.
- **No user-facing text** in Core. An error is a stable `ServiceErrorCode` plus parameters (`ServerName`, `Path`, `LineNumber`,
  `RealmlistKind`, `Field`, `RequiredBuild`, `ClientBuild`, `Consistency`); `ClientVersion`, `ConflictingFiles`, `Diagnostic` is English text for logs only.

```csharp
namespace RealmPortal.Core.Service;

public interface IPortalService
{
    // ---- clients & discovery ----
    ServiceResult<IReadOnlyList<ClientEntry>> ListClients();             // unreadable folder = entry with Error, not a failure
    ServiceResult<ClientEntry>                AddClient(string folder, string? name = null);
    ServiceResult<ClientEntry>                UpdateClient(UpdateClientRequest request);
    ServiceResult<Unit>                       RemoveClient(string clientId, bool deleteBackups = false);
    ServiceResult<ScanOutcome>                Scan(ScanRequest? request = null, IProgress<ScanProgress>? progress = null);
    ServiceResult<bool>                       IsGameRunning(string clientId);

    // ---- servers ----
    ServiceResult<IReadOnlyList<ServerEntry>> ListServers(string? clientId = null, bool compatibleOnly = false);
    ServiceResult<ServerEntry>                AddServer(NewServer server);
    ServiceResult<ServerEntry>                UpdateServer(UpdateServerRequest request);
    ServiceResult<Unit>                       RemoveServer(string serverId);

    // ---- switch, play, undo ----
    ServiceResult<SwitchOutcome>              SwitchServer(SwitchRequest request, IProgress<ServiceProgress>? progress = null);
    ServiceResult<SwitchOutcome>              Play(SwitchRequest request, IProgress<ServiceProgress>? progress = null);
    ServiceResult<UndoOutcome>                Undo(string? clientId = null); // single level; UndoConflict (+ ConflictingFiles) when files changed since
    ServiceResult<PortalStatus>               DiscardUndo(string? clientId = null); // gives up the pending undo; client files untouched; next state NothingToUndo

    // ---- backups ----
    ServiceResult<IReadOnlyList<BackupEntry>> ListBackups(string? clientId = null);
    ServiceResult<BackupEntry>                CreateBackup(string clientId, string? label = null);
    ServiceResult<RestoreBackupOutcome>       RestoreBackup(RestoreBackupRequest request);
    ServiceResult<RestoreBackupOutcome>       RestoreBackup(string clientId, string backupId, bool force = false);
    ServiceResult<Unit>                       DeleteBackup(string clientId, string backupId);

    // ---- status & settings ----
    ServiceResult<PortalStatus>               GetStatus();
    ServiceResult<StorageSettings>            GetStorageSettings();  // backup folder + retention
    ServiceResult<Unit>                       SetStorageSettings(StorageSettings settings);
    ServiceResult<LauncherSettings>           GetLauncherSettings();
    ServiceResult<Unit>                       SetLauncherSettings(LauncherSettings settings);
}
```

Ids: `ClientEntry.Id` is derived from the canonical folder path (`c` + 16 hex digits of the SHA-256 of the full path, lowercase on Windows), so it is stable and is
also the `installationId` of the backups. The server id is the `profileId` (32 lowercase hex digits). Both satisfy `ClientFilesRequest.IsValidId`.

---

## 1. Client & server discovery contract (`Scan`, `ClientOrigin`, `ClientServerExtractor`, `FirstScanCompleted`)

### 1.1 Signatures & models

```csharp
namespace RealmPortal.Core.Clients;

/// <summary>How a client installation was registered in <see cref="ClientStore"/>.</summary>
public enum ClientOrigin
{
    Manual     = 0, // Added or path-edited by the user
    Discovered = 1, // Found in a standard candidate folder (~/Games, C:\Games, etc.)
    Lutris     = 2, // Found via Lutris game YAML / runners config
    Wine       = 3, // Found inside a Wine / Bottles prefix (alias: WinePrefix)
    Steam      = 4, // Found via Steam libraryfolders.vdf / compatdata
    WinePrefix = Wine,
}
```

```csharp
namespace RealmPortal.Core.Service;

/// <summary>Optional parameters for <see cref="IPortalService.Scan"/>.</summary>
/// <param name="ImportServers">When true (default), runs <c>ClientServerExtractor</c> on each discovered client to import configured servers.</param>
/// <param name="AdditionalRoots">Optional extra root folders to inspect in addition to standard platform locations.</param>
public sealed record ScanRequest(
    bool ImportServers = true,
    IReadOnlyList<string>? AdditionalRoots = null);

/// <summary>Progress reported on the worker thread during <see cref="IPortalService.Scan"/>.</summary>
public sealed record ScanProgress(
    string CurrentSource,
    int DirectoriesInspected,
    int ClientsFound,
    int ServersImported);

/// <summary>Result of <see cref="IPortalService.Scan"/>.</summary>
/// <param name="AddedClients">Newly registered clients added to <c>clients.json</c> during this scan.</param>
/// <param name="ImportedServers">Newly extracted servers added to <c>servers.json</c> during this scan.</param>
/// <param name="AllClients">All registered clients after the scan finishes.</param>
public sealed record ScanOutcome(
    IReadOnlyList<ClientEntry> AddedClients,
    IReadOnlyList<ServerEntry> ImportedServers,
    IReadOnlyList<ClientEntry> AllClients);
```

### 1.2 Read-only guarantee & scanner sources (`ClientScanner`)

- **Strictly read-only on game clients:** Discovery (`ClientScanner` and `ClientServerExtractor`) opens external launcher configurations and client files strictly in read-only, non-following mode (`O_RDONLY | O_NOFOLLOW | O_NONBLOCK` on Unix, `FileAttributes.ReparsePoint` rejection on Windows) with bounded size caps (`PrivateStorage.ReadBounded`). It **never** creates, modifies, or deletes any file inside a candidate client folder or external launcher directory.
- **Candidate sources (`ClientOrigin`):**
  - `Lutris`: `~/.config/lutris/games/*.yml` and `~/.local/share/lutris/games/*.yml` (bounded line-based read `<= 256 KiB`, no arbitrary YAML deserialization).
  - `Steam`: `~/.steam/steam/steamapps/libraryfolders.vdf`, `~/.local/share/Steam/steamapps/libraryfolders.vdf`, `C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf` (`steamapps/common/*` and `steamapps/compatdata/*/pfx/drive_c/Program Files*/*`, depth `<= 2`).
  - `Wine` (`WinePrefix`): `$WINEPREFIX`, `~/.wine`, `~/.local/share/wineprefixes/*`, Bottles prefixes (`drive_c/Program Files*/*` and `drive_c/Games/*`, depth `<= 3`).
  - `Discovered`: standard folders (`~/Games`, `~/Juegos`, `/opt/games` on Linux; `C:\Games`, `D:\Games`, `C:\Program Files (x86)\World of Warcraft*` on Windows; depth `<= 3`, max 2,000 directories visited, skipping symlinks/reparse points and network/FUSE mounts).
- **Client validation & deduplication:**
  - Every candidate directory must pass `ClientPathSecurity.CanonicalizeTrustedFolder(candidatePath)` and `IClientVersionReader.Read(candidatePath)` (`GameExecutable.Find` + `PeClientVersionReader`, `<= 64 MiB` PE cap).
  - Deduplicated by `ClientEntry.Id` (`ClientIds.FromFolderPath(canonicalPath)`). Existing entries in `clients.json` are kept untouched (never overwriting `ClientOrigin.Manual` or a custom `Name`). When multiple sources reference the same new folder in one scan, precedence is `Lutris > Steam > Wine > Discovered`.

### 1.3 Server extraction & deduplication (`ClientServerExtractor`)

Whenever a client is discovered by `Scan()` (with `ImportServers == true`) **or** added manually via `AddClient(folder, name)`:

1. `ClientServerExtractor` inspects the client's existing configuration files in read-only mode (`<= 1 KiB` for `realmlist.wtf`, `<= 64 KiB` for `WTF/Config.wtf`) using the version's file layout (`Data/<locale>/realmlist.wtf`, root `realmlist.wtf`, and `WTF/Config.wtf`):
   - Extracts `set realmlist <host>` and optional `set patchlist <host>` from `realmlist.wtf`.
   - Extracts `SET realmList "<host>"`, `SET patchlist "<host>"`, `SET portal "<host>"` (6.x+), `SET realmName "<realm>"`, and `SET accountName "<account>"` from `WTF/Config.wtf`.
2. Validates the extracted host and patchlist with `RealmlistAddress.ValidateRealmlistHost` and `RealmlistAddress.ValidatePatchlistValue`, and validates metadata (`Realm`, `Account`) with `ServerValidator`. Unparseable, missing, or stock official placeholder hosts (`*.battle.net`, `*.worldofwarcraft.com`) are skipped silently without failing `Scan()` or `AddClient()`.
3. **Deduplication key:**
   - Normalized tuple **`(NormalizedHost, NormalizedRealm, RequiredClientBuild)`**, compared case-insensitively (`StringComparison.OrdinalIgnoreCase`) on `NormalizedHost` (trimmed, lowercase `host` or `host:port`) and `NormalizedRealm` (trimmed `Realm ?? ""`), with `RequiredClientBuild == client.Version.Build`.
   - If a matching server already exists in `ServerStore`, no duplicate server is created; if that existing server has `ClientId == null`, its `ClientId` is linked to `client.Id`.
   - Otherwise, a new `Server` is added to `ServerStore` with `RequiredClientBuild = client.Version.Build`, `ClientId = client.Id`, and an auto-generated unique `Name` (`PortalService.CreateImportedServerName`): `Realm` (control characters stripped, trimmed) when non-empty, e.g. `"Example Realm"`; otherwise `Host`, e.g. `"realm.example.test"` (revised by #98: the card already shows the host, so the name does not repeat it); capped at `<= 64` characters (`Server.MaxNameLength`) and disambiguated case-insensitively with `" #2"`, `" #3"`, ... (truncating the base prefix so `base + suffix <= 64` characters; see **Approved decisions #1**).
   - If `state.json` has no active server recorded for `client.Id` (`active_by_client[client.Id]` is unset), the extracted (or matched) server is marked active for `client.Id` in `state.json` (and if global `active_id` is `null`, sets `active_id` and writes `activo.json`).

### 1.4 First-launch flag (`FirstScanCompleted`)

`UiPreferences` (`ui-preferences.json`, managed by `UiPreferencesStore` with `0600` private permissions and bounded reads) adds `FirstScanCompleted`:

```csharp
public sealed record UiPreferences(
    string? LanguageCode = null,
    IReadOnlyList<string>? ClientsInUse = null,
    bool FirstScanCompleted = false)
{
    public HashSet<string> InUseSet => ClientsInUse?.ToHashSet(StringComparer.Ordinal) ?? [];
}
```

- On App startup, if `!preferences.FirstScanCompleted`, the App runs `Service.Scan()` once in the background, adds newly discovered client IDs to `ClientsInUse`, and saves `preferences with { FirstScanCompleted = true }`.
- Clicking **Rescan** (`ClientsViewModel.RescanCommand`) or running `realm-portal scan` in the CLI invokes `Service.Scan()` on demand at any time. Re-running `Scan()` is strictly idempotent.

---

## 2. CRUD models & contracts for clients and servers

### 2.1 Models (`ClientEntry`, `UpdateClientRequest`, `UpdateServerRequest`)

```csharp
namespace RealmPortal.Core.Service;

/// <summary>A game client folder known to the app.</summary>
/// <param name="Id">Stable id derived from the canonical folder path (<c>c</c> + 16 lowercase hex chars); also the installation id of the backups.</param>
/// <param name="Path">The client folder (the one with <c>Wow.exe</c>).</param>
/// <param name="Version">The client version; <c>null</c> when it could not be read (then <see cref="Error"/> says why).</param>
/// <param name="Error">Why the version could not be read (<c>PathNotFound</c>, <c>ClientExecutableNotFound</c>, <c>ClientNotAPortableExecutable</c>, ...). An unreadable client is a normal list entry, not a failure of the call.</param>
/// <param name="Name">User-facing display name (defaults to <c>WoW {Version}</c> or folder name when <c>null</c>; max 64 chars, no control/format chars).</param>
/// <param name="Origin">How the client was added (<see cref="ClientOrigin"/>).</param>
/// <param name="IsRunning">True when <see cref="IPortalService.IsGameRunning"/> detects a live process for this client folder.</param>
public sealed record ClientEntry(
    string Id,
    string Path,
    ClientVersion? Version,
    ServiceError? Error,
    string? Name = null,
    ClientOrigin Origin = ClientOrigin.Manual,
    bool IsRunning = false);

/// <summary>Request to edit an existing registered client.</summary>
/// <param name="ClientId">Existing client id to update (<see cref="ClientEntry.Id"/>). Unknown id: <see cref="ServiceErrorCode.ClientNotFound"/>.</param>
/// <param name="Name">New display name (<c>null</c> or empty resets to default display name; max 64 chars, validated against control/format characters).</param>
/// <param name="Folder">New client folder path (<c>null</c> keeps the current path).</param>
public sealed record UpdateClientRequest(
    string ClientId,
    string? Name = null,
    string? Folder = null);

/// <summary>Request to edit an existing server in <see cref="Servers.ServerStore"/>.</summary>
/// <param name="ServerId">Immutable 32-hex server id (<see cref="ServerEntry.Id"/>). Unknown id: <see cref="ServiceErrorCode.ServerNotFound"/>.</param>
/// <param name="Name">Display name (unique case-insensitively across all other servers).</param>
/// <param name="Host">Realmlist host, e.g. <c>logon.example.org</c> or <c>10.0.0.5:3724</c>.</param>
/// <param name="Realm">Realm name to preselect in the login screen; <c>null</c> for none.</param>
/// <param name="PatchlistHost">Value of an optional <c>set patchlist</c> line; <c>null</c> for none.</param>
/// <param name="RequiredClientBuild">Client build the server needs (e.g. 12340); <c>null</c> for any.</param>
/// <param name="Account">Remembered account name (never a password); <c>null</c> for none.</param>
/// <param name="ClientId">Preferred client for this server (<see cref="ClientEntry.Id"/>); <c>null</c> for none. Unknown id: <see cref="ServiceErrorCode.ClientNotFound"/>.</param>
/// <param name="ExtraRealmlistLines">Extra realmlist lines after <c>set realmlist</c>.</param>
public sealed record UpdateServerRequest(
    string ServerId,
    string Name,
    string Host,
    string? Realm = null,
    string? PatchlistHost = null,
    int? RequiredClientBuild = null,
    string? Account = null,
    string? ClientId = null,
    IReadOnlyList<string>? ExtraRealmlistLines = null);
```

### 2.2 Client CRUD semantics (`AddClient`, `UpdateClient`, `RemoveClient`)

- **`clients.json` persistence (`StoredClient`):**
  ```json
  {
    "version": 1,
    "clients": [
      {
        "id": "c0123456789abcdef",
        "path": "/home/carlos/Games/WoW-335a",
        "name": "Wrath 3.3.5a",
        "origin": "lutris"
      }
    ]
  }
  ```
  `name` (`null` or 1–64 chars without control/format/quote/backslash chars) and `origin` (default `"manual"` when omitted in legacy v1 files) are persisted under `ConfigDirectoryLock` with `PrivateStorage.PrivateFilePermissions` (`0600`).
- **`AddClient(string folder, string? name = null)`:**
  - Canonicalizes `folder` (`ClientPathSecurity.CanonicalizeTrustedFolder`), reads `ClientVersion`, stores the client with `Origin = ClientOrigin.Manual`, and runs `ClientServerExtractor` to import any server already configured in that client's `realmlist.wtf` / `Config.wtf`.
- **`UpdateClient(UpdateClientRequest request)`:**
  - Fails with `ClientNotFound` if `request.ClientId` is not registered.
  - Fails with `GameRunning` (`Path = existing.Path`) if `IsGameRunning(request.ClientId)` is true.
  - Validates `request.Name` (when non-null: `<= 64` chars, no control/format/quote/backslash characters, else `InvalidClientInput` / `InvalidServerInput` with `Field = "name"`).
  - If `request.Folder` is provided and canonicalizes to a new path (`newId != request.ClientId`):
    1. Checks `IsGameRunning` on the new folder (`GameRunning` if running) and validates the executable version (`PathNotFound`, `ClientExecutableNotFound`, `ClientNotAPortableExecutable`, `ClientNoVersionResource`, `ClientUnreadable`).
    2. Fails with `ClientAlreadyExists` if `newId` is already registered in `ClientStore`.
    3. Under `ConfigDirectoryLock`, replaces `oldId` with `newId` (`Origin = ClientOrigin.Manual`), updates any server in `servers.json` whose `ClientId == oldId` to `newId`, migrates `active_by_client[oldId]` to `active_by_client[newId]` in `state.json`, and clears any pending undo in `last-switch.json` / `state.json` bound to `oldId` (because backup manifests in `<backupRoot>/<oldId>` are bound to the old `client_root`).
- **`RemoveClient(string clientId, bool deleteBackups = false)`:**
  - Fails with `ClientNotFound` if `clientId` is not registered.
  - Fails with `GameRunning` if `IsGameRunning(clientId)` is true.
  - Under `ConfigDirectoryLock`: removes `clientId` from `clients.json`, clears `ClientId = null` on any server in `servers.json` that referenced `clientId` (servers themselves are not deleted), removes `active_by_client[clientId]` from `state.json`, and clears pending undo if it belonged to `clientId`.
  - When `deleteBackups == false` (default), `<backupRoot>/<clientId>` is kept on disk. When `deleteBackups == true`, acquires `InstallationLock(clientId)` and deletes managed backups under `<backupRoot>/<clientId>`. The real game folder is never touched.

### 2.3 Server CRUD semantics (`AddServer`, `UpdateServer`, `RemoveServer`)

- **`UpdateServer(UpdateServerRequest request)`:**
  - Validates all fields via `Server.Create(..., id: request.ServerId)` and `ServerValidator.Validate` (identical host, port, realmlist, length, and forbidden-character checks as `AddServer` and `ServerStore` load).
  - Fails with `ServerNotFound` if `request.ServerId` does not exist; fails with `ClientNotFound` if `request.ClientId` is non-null and not in `ClientStore`; fails with `ServerNameTaken` (`ServerName = request.Name`) if another server (`Id != request.ServerId`) already uses `request.Name` case-insensitively.
  - **Running check:** If `request.ServerId` is currently active on any client `C` where `IsGameRunning(C) == true` and any connection/file field changed (`Host`, `Realm`, `PatchlistHost`, `ExtraRealmlistLines`, `RequiredClientBuild`), refuses with `ServiceErrorCode.GameRunning` (`ServerName = existing.Name`, `Path = C.Path`).
  - **Active projection & drift update:** Under `ConfigDirectoryLock`, saves `servers.json`. If `state.ActiveId == request.ServerId`, rewrites `activo.json` atomically so the projection stays in sync (`ProjectionInSync == true`). Client files on disk are not rewritten silently during metadata edit; if `Host`, `Realmlist`, or `Realm` changed for an active client, `GetStatus().ClientsNotMatchingActive` reports that client as drifted so the user can re-switch/repair or press Play.
- **`RemoveServer(string serverId)`:**
  - Fails with `ServerNotFound` if `serverId` is not in `ServerStore`.
  - **Running check:** If `serverId` is currently active on any client `C` with `IsGameRunning(C) == true`, refuses with `ServiceErrorCode.GameRunning`.
  - Under `ConfigDirectoryLock`: removes `serverId` from `servers.json`, clears any `active_by_client[c] == serverId` entry in `state.json`, clears any pending undo referencing `serverId` (`state.json` and `last-switch.json`), and if global `active_id == serverId`, sets `active_id = null` and deletes `activo.json`. Existing backups in `<backupRoot>/<clientId>` are preserved.

---

## 3. Multi-client active state (`active_by_client`) & per-client concurrency invariant

### 3.1 `state.json` schema with `active_by_client` and global `active_id`

`ActiveServerManager` (`state.json` in `configDirectory`) tracks the active server and one-level undo **per client** in `active_by_client` (and `undo_by_client`) alongside the global `active_id` used for `activo.json` projection and CLI state-only compatibility:

```json
{
  "version": 1,
  "active_id": "a1b2c3d4e5f60718293a4b5c6d7e8f90",
  "undo": {
    "server_id": "0123456789abcdef0123456789abcdef"
  },
  "active_by_client": {
    "c0123456789abcdef": "a1b2c3d4e5f60718293a4b5c6d7e8f90",
    "cfedcba9876543210": "11112222333344445555666677778888"
  },
  "undo_by_client": {
    "c0123456789abcdef": {
      "server_id": "0123456789abcdef0123456789abcdef"
    }
  }
}
```

- **Backward-compatible format (`version: 1`):**
  - `active_id` (`string?`): the most recently activated server globally, projected to `activo.json` in `projectionDirectory`.
  - `active_by_client` (`Dictionary<string, string>?`): maps `clientId -> serverId` for each registered client that has an active server. Omitted or empty in legacy `state.json` files (treated as empty dictionary). While the map is empty a client with no entry falls back to the global `active_id`; once any per-client entry exists, a client with no entry has no active server (it does not inherit another client's global selection).
  - `undo` (`{ "server_id": string? }?`) and `undo_by_client` (`Dictionary<string, UndoRecord>?`): tracks the previous `server_id` globally and per `clientId`.
- **`ListServers(string? clientId = null, bool compatibleOnly = false)`:**
  - When `clientId` is provided, `ServerEntry.IsActive` is true for the server matching `active_by_client[clientId]` (or global `active_id` only while `active_by_client` is empty, for legacy files), and `ServerEntry.IsCompatible` is evaluated against `clientId`'s `ClientVersion.Build`.
  - When `clientId` is `null`, `ServerEntry.IsActive` reflects global `active_id` (or whether the server is active for its preferred `Server.ClientId`).
- **`PortalStatus`:**
  ```csharp
  public sealed record PortalStatus(
      ServerEntry? Active,
      bool CanUndo,
      bool ProjectionInSync,
      string BackupFolder,
      IReadOnlyList<ClientEntry> ClientsNotMatchingActive,
      IReadOnlyDictionary<string, ServerEntry>? ActiveByClient = null);
  ```

### 3.2 Per-client concurrency invariant

> **Invariant:** *One client runs at most one game process; another distinct client can switch server and launch in parallel.*

1. **Folder-scoped process detection (`IGameProcessProbe.IsGameRunning(string clientRoot)`):**
   - `IsGameRunning` checks whether a live game process (`Wow.exe`, `Wow-64.exe`, `WowB.exe`, `WowT.exe`, `WowClassic.exe`, including Wine/Proton launches whose executable path or command-line argument resolves inside canonical `clientRoot` on a strict path-segment boundary) is running for **that specific `clientRoot`**.
   - A client in `/home/user/Games/WoW-335a` never matches `/home/user/Games/WoW-335a-copy` or `/home/user/Games/WoW-Classic`.
2. **Per-client lock & running check:**
   - File-mutating operations on a client `C` acquire the per-client cross-process `InstallationLock(C.Id)` (`<backupRoot>/<clientId>/.lock`) and verify `_probe.IsGameRunning(C.Path) == false`.
   - `InstallationLock(C.Id)` is **released before `ServiceStep.Launch` starts**, so a running game process never holds a file lock on `.realm-portal.lock` or `<backupRoot>/<clientId>/.lock`.
3. **What is blocked vs. allowed while Client `A` is running on Server `X`:**

| Operation | Target = Running Client `A` (or active Server `X` on `A`) | Target = Idle Client `B` (`B != A`) / Server `Y` |
|---|---|---|
| `SwitchServer(clientId, serverId)` | **Blocked** (`ServiceErrorCode.GameRunning`, `Consistency.Unchanged`) | **Allowed** (switches Client `B`, updates `active_by_client[B]` and `activo.json`) |
| `Play(clientId, serverId)` | **Blocked** (`ServiceErrorCode.GameRunning`, `Consistency.Unchanged`) | **Allowed** (switches Client `B` and launches Client `B` in parallel with Client `A`) |
| `Undo(clientId)` | **Blocked** (`ServiceErrorCode.GameRunning`) when undo targets Client `A` | **Allowed** when undo targets idle Client `B` |
| `UpdateClient` / `RemoveClient` | **Blocked** (`ServiceErrorCode.GameRunning`) for Client `A` | **Allowed** for Client `B` |
| `CreateBackup` / `RestoreBackup` / `DeleteBackup` | **Blocked** (`ServiceErrorCode.GameRunning`) for Client `A` | **Allowed** for Client `B` |
| `UpdateServer(serverId)` (connection fields) / `RemoveServer(serverId)` | **Blocked** (`ServiceErrorCode.GameRunning`) for Server `X` | **Allowed** for Server `Y` |

---

## 4. First-class backup management API & small-backup policy

### 4.1 Models & signatures

```csharp
namespace RealmPortal.Core.Service;

/// <summary>Metadata of a single client-files backup snapshot in <c>&lt;backupRoot&gt;/&lt;clientId&gt;/&lt;backupId&gt;</c>.</summary>
/// <param name="BackupId">Timestamp + random suffix id (<c>yyyyMMddTHHmmssfffZ-&lt;8hex&gt;</c>).</param>
/// <param name="ClientId">Installation id (<see cref="ClientEntry.Id"/>).</param>
/// <param name="ClientPath">Canonical client root folder recorded in <c>manifest.json</c>.</param>
/// <param name="ServerId"><c>profile_id</c> recorded in the manifest (<c>"manual"</c> or active server id for manual snapshots).</param>
/// <param name="ServerName">Resolved server display name if <paramref name="ServerId"/> is in <c>ServerStore</c>, or custom label.</param>
/// <param name="CreatedAt">UTC timestamp when the backup was created.</param>
/// <param name="TotalBytes">Sum of backed-up file sizes in bytes (typically &lt; 16 KiB).</param>
/// <param name="Files">Relative client paths included in the snapshot (<c>realmlist.wtf</c>, <c>Data/&lt;locale&gt;/realmlist.wtf</c>, <c>WTF/Config.wtf</c>).</param>
/// <param name="UndoneAt">UTC timestamp if this backup was already undone/restored; <c>null</c> otherwise.</param>
/// <param name="IsManual">True when created via <see cref="IPortalService.CreateBackup"/> rather than an automatic switch.</param>
/// <param name="Label">Optional user/system note (e.g. manual label or <c>"pre-restore"</c> safety snapshot).</param>
/// <param name="WasDeduplicated">True when <see cref="IPortalService.CreateBackup"/> reused the latest identical backup instead of writing duplicate files.</param>
public sealed record BackupEntry(
    string BackupId,
    string ClientId,
    string ClientPath,
    string ServerId,
    string? ServerName,
    DateTimeOffset CreatedAt,
    long TotalBytes,
    IReadOnlyList<string> Files,
    DateTimeOffset? UndoneAt = null,
    bool IsManual = false,
    string? Label = null,
    bool WasDeduplicated = false);

/// <summary>Request to restore a specific backup for a client.</summary>
/// <param name="ClientId">Target registered client (<see cref="ClientEntry.Id"/>).</param>
/// <param name="BackupId">Backup snapshot id to restore.</param>
/// <param name="Force">When false, modified client files fail with <see cref="ServiceErrorCode.UndoConflict"/> without touching the client. When true ("Restore anyway"), creates a safety backup of current files first and then overwrites.</param>
public sealed record RestoreBackupRequest(
    string ClientId,
    string BackupId,
    bool Force = false);

/// <summary>Result of a completed <see cref="IPortalService.RestoreBackup"/> call.</summary>
/// <param name="RestoredBackupId">The backup id whose bytes were restored.</param>
/// <param name="SafetyBackupId">Id of the pre-restore safety backup created when <c>Force == true</c>; <c>null</c> when none was needed or deduplicated.</param>
/// <param name="ActiveServer">Active server for the client after restore.</param>
/// <param name="Files">Per-file restore outcomes.</param>
public sealed record RestoreBackupOutcome(
    string RestoredBackupId,
    string? SafetyBackupId,
    ServerEntry? ActiveServer,
    IReadOnlyList<UndoFileResult> Files);
```

Corresponding methods on `IClientFilesApplier`:
- `IReadOnlyList<BackupEntry> ListBackups(string? installationId = null)`
- `BackupEntry CreateBackup(string installationId, string clientRoot, string? label = null)`
- `RestoreBackupOutcome RestoreBackup(string installationId, string clientRoot, string backupId, bool force = false)`
- `void DeleteBackup(string installationId, string backupId)`
- `void PruneBackups(string? installationId = null, int? keepBackups = null)`

### 4.2 Behavior & safety invariants

1. **`ListBackups(string? clientId = null)`:**
   - Enumerates `<backupRoot>/<clientId>/<backupId>/manifest.json` (or all registered client IDs when `clientId == null`), sorted newest-first (`CreatedAt` / `BackupId` descending).
   - Uses bounded reads (`<= 256 KiB` for `manifest.json`) and ignores non-directory entries or `.lock` files. Corrupt manifests are skipped or surfaced with diagnostics without aborting the listing of valid backups.
2. **`CreateBackup(string clientId, string? label = null)`:**
   - Fails with `ClientNotFound` if `clientId` is unknown, or `GameRunning` if `IsGameRunning(clientId)` is true.
   - Acquires `InstallationLock(clientId)`. Reads existing managed files (`realmlist.wtf`, `Data/<locale>/realmlist.wtf`, `WTF/Config.wtf`) with `O_NOFOLLOW | O_NONBLOCK`.
   - If the newest existing backup for `clientId` already has the exact same set of managed paths, `sha256_before`, permissions, and `.symlink` targets, returns that existing `BackupEntry` with `WasDeduplicated = true` instead of writing a duplicate directory.
   - Otherwise writes `<backupId>/files/<n>`, optional `.symlink` sidecars, and `manifest.json` (`is_manual: true`, `label`), then runs `PruneBackups(clientId, KeepBackups)`.
3. **`RestoreBackup(RestoreBackupRequest request)` / `RestoreBackup(string clientId, string backupId, bool force = false)`:**
   - Fails with `ClientNotFound` if `clientId` is unknown, `BackupNotFound` if `backupId` is invalid or missing, or `GameRunning` if `IsGameRunning(clientId)` is true.
   - Acquires `InstallationLock(clientId)`. Validates `manifest.json` (`RequireMatchingRoots` against the registered client path, `ValidateManagedRelativePath`, `EnsureBackupFilesReferenced`) and verifies all backup file hashes (`SymlinkSnapshot.OpenNoFollowForRead`).
   - **Atomic pre-flight conflict check (all files checked before any write):** Both `Undo()` and `RestoreBackup(..., force: false)` inspect **all** target client files in the preparation pass before modifying the first file on disk. If any file changed since the snapshot (`currentHash != sha256_after && currentHash != sha256_before`), the call returns `ServiceErrorCode.UndoConflict` with `ConflictingFiles` and `Consistency.Pending`, leaving the client **100% untouched** (no partial restore).
   - **Forced restore (`Force = true`, "Restore anyway"):** First takes an automatic safety backup of the client's current managed files (`Label = "pre-restore"`, yielding `SafetyBackupId`), then restores all files from `backupId` byte-for-byte (including permissions and validated in-client symlinks) and marks `backupId` undone. If `backupId` was the pending undo in `last-switch.json`, updates `ActiveServerManager` and clears the pending journal entry.
4. **`DeleteBackup(string clientId, string backupId)`:**
   - Fails with `ClientNotFound` if `clientId` is unknown, `BackupNotFound` if `backupId` fails `BackupIdFormat.IsValid` or does not exist, or `GameRunning` if `IsGameRunning(clientId)` is true.
   - Acquires `InstallationLock(clientId)`, verifies `<backupRoot>/<clientId>/<backupId>` is a regular directory inside `<backupRoot>/<clientId>` (no symlinks/reparse points), and deletes it.
   - If `last-switch.json` referenced `(clientId, backupId)`, clears `last-switch.json` and calls `ActiveServerManager.DiscardPendingUndo(clientId)` under `ConfigDirectoryLock`.

### 4.3 Small-backup policy

- **Strictly managed config files only:** Only `realmlist.wtf`, `Data/<locale>/realmlist.wtf`, and `WTF/Config.wtf` (plus `.symlink` target sidecars) are ever backed up. Game data archives (`*.MPQ`, `Data/casc`), `Cache/`, `Interface/`, `WTF/Account/`, `Logs/`, and `Screenshots/` are never touched or copied. A normal backup is `< 16 KiB` on disk.
- **1 MiB per-file cap (`MaxManagedConfigBytes = 1_048_576`):** Backing up or reading a client config file larger than 1 MiB fails immediately with `VerifyFailed` / `InvalidRealmlist` (`Consistency.Unchanged`), preventing anomalous or hostile multi-megabyte files from bloating `<backupRoot>`.
- **Identical-snapshot deduplication:** Consecutive switches or manual backups that produce identical file hashes reuse the latest snapshot (`WasDeduplicated = true`).
- **Retention (`KeepBackups`):** Controlled by `StorageSettings.KeepBackups` (default `10`, valid range `1..50`). After creating a backup, `PruneBackups` deletes the oldest non-pending backup directories for that client beyond `KeepBackups`.

---

## 5. Storage & launcher settings contract

```csharp
namespace RealmPortal.Core.Service;

/// <summary>Where backups are kept and how many snapshots per client are retained, set from the Settings screen.</summary>
/// <param name="BackupFolder">Root folder for backups. Validated for existence, no symlinks on path, and write access.</param>
/// <param name="KeepBackups">Number of backups to retain per client installation (default 10, range 1..50). Out-of-range values fail with <see cref="ServiceErrorCode.InvalidStorageSettings"/>.</param>
public sealed record StorageSettings(
    string BackupFolder,
    int KeepBackups = StorageSettings.DefaultKeepBackups)
{
    public const int DefaultKeepBackups = 10;
    public const int MinKeepBackups = 1;
    public const int MaxKeepBackups = 50;
}
```

`storage-settings.json` in `configDirectory` (`version: 1`, written with `0600` permissions):

```json
{
  "version": 1,
  "backup_folder": "/home/carlos/.local/share/realm-portal/backups",
  "keep_backups": 10
}
```

- When `keep_backups` is omitted in an existing v1 `storage-settings.json`, `GetStorageSettings()` defaults `KeepBackups` to `10`.
- `SetStorageSettings(StorageSettings settings)` validates `BackupFolder` (`PathNotFound`, `PermissionDenied`, `ReadOnlyFs`, `PathEscapesClient`) and validates `KeepBackups` in `1..50` (`InvalidStorageSettings` with `Field = "keepBackups"` when out of range), saves `storage-settings.json` under `ConfigDirectoryLock`, and triggers `PruneBackups(keepBackups: settings.KeepBackups)`.

`LauncherSettings(CommandTemplate, Arguments, Environment, Prefix)` is a `GameLaunchSpec` without the executable and working directory; the service
completes it with the selected client's `Wow.exe` and folder (`ToSpec`). Persisted in `launcher-settings.json` (`0600`); `SetLauncherSettings` validates by resolving it
(`InvalidLauncherSettings`), including rejecting dangerous dynamic-loader environment variables (`LD_PRELOAD`, `LD_LIBRARY_PATH`, `DYLD_INSERT_LIBRARIES`, `DYLD_LIBRARY_PATH`). Nothing goes through a shell (see [launch.md](launch.md)).

---

## 6. Steps, progress, and undo journal consistency

### 6.1 `SwitchServer` and `Play` steps

| `ServiceStep` | What runs | Used by |
|---|---|---|
| `Plan` | `applier.Plan(new ClientFilesRequest(installationId, profileId, clientRoot, clientVersion, server.Realmlist, server.Realm))` (dry run) | switch, play |
| `WriteFiles` | `applier.Apply(plan)`: backup first, then `realmlist.wtf` / `Config.wtf`. **One indivisible call**, so "Backup" and "Realmlist" cannot be reported separately | switch, play |
| `Activate` | `ActiveServerManager.Switch` (`state.json` [`active_id` + `active_by_client[clientId]`] + `activo.json`) | switch, play |
| `Launch` | `IGameLaunchService.Launch(GameLaunchSpec)` (after releasing `InstallationLock`) | play |

Each step emits `Started`, then `Completed`, or `Failed` with the `ServiceError`. After a failure no later step starts. A plan with no edits (the client
already has this configuration) still reports `WriteFiles` Started/Completed with `SwitchOutcome.BackupId == null`.

**Re-switching the server that is already active** for the chosen client is valid (not an error). The service still runs `Plan`. When the plan has edits
(client files drifted), it applies them as a repair. When the plan is empty, it still reports `WriteFiles` Started/Completed with `BackupId == null`. In both
cases it does **not** replace the pending single-level undo or the `last-switch.json` journal with a no-op entry: a no-op re-switch must not erase the undo
from the previous real switch to a different server. `ActiveServerManager.Switch` is still called (projection refresh; undo in `state.json` is unchanged when
the server was already active for that client). In `SwitchOutcome`, `Previous` equals `Active` (the same server). When the re-switch repairs drifted files, the applier may
create a new backup, but the journal's `backup_id` and `manifest_sha256` are **not** replaced (undo still restores the bytes from the original switch).

**There is no `Account` step.** The applier never writes the account name (`Config.wtf` `accountName` is explicitly left alone, and `ClientFilesRequest` has
no account). `Server.Account` is only a remembered label.

### 6.2 Consistency between the two undo stacks (`state.json` & `last-switch.json`)

Two stacks must move together: the applier's backups (per installation) and `ActiveServerManager`'s one-level undo in `state.json`. The service
keeps a tiny journal of the **last switch** (`last-switch.json` in the config folder: optional `client_id`, optional `backup_id`, optional `manifest_sha256`, and optional `by_client` map for per-client undo)
so undo knows whether **that** switch changed client files. Every successful switch writes the journal under `ConfigDirectoryLock`, including
CLI switches without `--client-dir` (`client_id`, `backup_id`, and `manifest_sha256` all omitted). A later state-only switch **replaces** the global journal entry so global undo never
replays a backup from an earlier `--client-dir` switch. Re-switching the **same** server to repair drifted files creates a new backup in the applier
store but **does not** replace the journal's `backup_id` or `manifest_sha256` (undo still restores the original switch's bytes).
`manifest_sha256` is the SHA-256 of that backup's `manifest.json` bytes. When the field is present, undo passes it to `applier.Undo`, which refuses
with `HashMismatch` if those bytes changed, before it writes or deletes a client file. The hash lives in the journal, outside the backup folder,
because every file in the backup folder can be edited together. Journals written before this field existed omit it; undo then does not check the
manifest hash, and those journals keep working as before. A value that is present and is not 64 lowercase hex digits makes the journal invalid (`StoreCorrupt`).

- **Switch:** plan, apply, then activate. If *activate* fails after *apply* succeeded, the service runs `applier.Undo` (the backup it just made) and
  returns the activation error with `RolledBack`; if that rollback fails or reports conflicts the error is `InconsistentState` / `Inconsistent`.
  If plan or apply fails nothing changed (the applier reverts itself): `Unchanged`. The journal is written only after both succeed.
- **Undo:** pre-flight first, changing nothing: `state.json` has a pending undo, the previous server still exists and its realmlist is valid, the target client is not
  running when client files would be touched. Then `applier.Undo` for the journal's `backup_id`, passing `manifest_sha256` when the journal has it, only when that id is set (skipped for state-only switches),
  then `ActiveServerManager.Undo`. Undo **never** writes client files for state-only switches (`client_id` omitted in the journal): registered clients
  may still drift from `activo.json` until the user runs `switch <server> --client-dir <folder>`. The CLI prints whether client files were updated.
  If the second step fails after file undo succeeded, the service re-applies the active server's
  files and reports `RolledBack`; if that fails, `InconsistentState`.
- **Conflicts (all-or-nothing pre-check):** Both `applier.Undo` and `applier.RestoreBackup(..., force: false)` check all target files in the manifest before writing any file. When any file differs from what the switch wrote, **no client file is modified**, the app state is **not** touched, and the undo stays pending. The call fails with `UndoConflict`, `ConflictingFiles`, and `Consistency.Pending`.
- **Discard & Restore-anyway:**
  - `DiscardUndo(string? clientId = null)` gives up the pending undo without touching client files or the active server. The next state is `NothingToUndo` (`CanUndo == false`). With no `clientId`, only the global/legacy `last-switch.json` entry is cleared; the `by_client` entries and matching `undo_by_client` records of other clients are preserved so their undo stays usable.
  - **Restore anyway** on a conflict invokes `RestoreBackup(new RestoreBackupRequest(clientId, backupId, Force: true))` (which snapshots current files into a pre-restore safety backup first and then restores `backupId`).
- **Play** = switch + launch. When launch fails after activate succeeded, the switch stays done (the client is configured, the server is active, undo still works) and the call returns
  `LaunchFailed` with `Consistency.AppliedLaunchFailed` (not `Unchanged`).

---

## 7. Errors (`ServiceErrorCode` mapping)

| Source | Codes |
|---|---|
| `ClientFilesException.Code` | mapped **by name**: `GameRunning`, `PermissionDenied`, `ReadOnlyFs`, `PathNotFound`, `PathEscapesClient`, `HashMismatch`, `VerifyFailed`, `UnsupportedClient` (with `ClientVersion`), `InvalidRealmlist`, `NothingToUndo`, `BackupNotFound`, `ProcessCheckFailed`. A test fails if the applier gets a code the service lacks. |
| `InvalidRealmlistException` | `InvalidRealmlist` with `RealmlistKind`, `LineNumber`, `ServerName` |
| `ClientVersionException.Kind` | `ClientExecutableNotFound`, `ClientNotAPortableExecutable`, `ClientNoVersionResource`, `ClientUnreadable` (a missing folder: `PathNotFound`), with `Path` |
| `StoreException` | `StoreCorrupt`, `ServerNameTaken`, `ServerNotFound`, `ClientNotFound`, `ClientAlreadyExists`, `NothingToUndo`, `UndoTargetMissing`, `StoreLocked` (the config-directory lock `.realm-portal.lock` is still held by another process; nothing changed, retry) |
| undo or restore backup made for another client folder (manifest `client_root` differs from the registered client) | `PathEscapesClient`, `Diagnostic` names both folders; nothing is written |
| managed path outside the allow-list, traversal/absolute/backslash/drive paths, or a managed folder linked outside the client | `PathEscapesClient`; nothing is written or deleted |
| undo or non-forced `RestoreBackup` finds changed client files | `UndoConflict` with `ConflictingFiles`, `Consistency.Pending` (client files 100% untouched) |
| backup id does not exist or has invalid format in `RestoreBackup` / `DeleteBackup` | `BackupNotFound` with `Path`, `Consistency.Unchanged` |
| backup folder cannot be used | `PathNotFound`, `PermissionDenied`, `ReadOnlyFs` |
| `KeepBackups` out of `1..50` range in `SetStorageSettings` | `InvalidStorageSettings` with `Field = "keepBackups"`, `Consistency.Unchanged` |
| `ArgumentException` from `Server.Create` / `ServerValidator` / `UpdateServer` | `InvalidServerInput` with `Field` (`"name"`, `"host"`, `"realm"`, `"account"`, `"build"`) |
| `ArgumentException` from `UpdateClient` (`Name` validation) | `InvalidClientInput` (or `InvalidServerInput`) with `Field = "name"` |
| `GameLaunchSpec.Resolve` / launcher settings | `InvalidLauncherSettings` |
| process start failure after a successful switch (`Play` only) | `LaunchFailed` with `Consistency.AppliedLaunchFailed` (switch and active server stay; undo remains available) |
| `Play` fails in `Plan`, `WriteFiles` or `Activate` (before `Launch` starts) | that step's own code (never `LaunchFailed`) with `Consistency.Unchanged` or `RolledBack`; if the rollback after a failed `Activate` also fails, `InconsistentState` with `Consistency.Inconsistent`, exactly as for `SwitchServer` |
| incompatible build without `Force` | `IncompatibleClient` with `RequiredBuild`, `ClientBuild` |
| anything else | `Unexpected` (with `Diagnostic`) |

`Consistency` tells the UI what a failed multi-step call left behind: `Unchanged`, `RolledBack` (steps ran and were put back), `Inconsistent`
(`InconsistentState`: even the rollback failed; tell the user to review the client), `Pending` (only `UndoConflict`: stopped on purpose before touching any file, can be retried, forced via `RestoreBackup(force: true)`, or discarded via `DiscardUndo()`), or
`AppliedLaunchFailed` (only `Play`: plan, apply and activate succeeded; launch failed — the client is configured, the new server is active, and undo is still available).

---

## Approved decisions (Approval Gate — Carlos)

1. **Auto-naming rule for servers imported during client scan (`ClientServerExtractor` / `PortalService.CreateImportedServerName`) — Approved: Option A.**
   - **Decision:** When `Scan()` or `AddClient()` imports a server from a client's `realmlist.wtf` / `WTF/Config.wtf`:
     - **Revised by #98 (1.0.1):** if `Realm` (stripped of control characters and trimmed) is non-empty, use `Realm` alone (e.g., `"Example Realm"`); if `Realm` is empty, use `Host` alone (e.g., `"realm.example.test"`). The server card already shows the host under the name, so the previous `"{Realm} ({Host})"` / `"{Host} ({Major}.{Minor}.{Build})"` forms repeated it.
     - Trim and cap the base name at `<= 64` characters (`Server.MaxNameLength`, falling back to `"Imported server"` if empty).
     - On case-insensitive name collision in `ServerStore`, append `" #2"`, `" #3"`, ..., truncating the base prefix to `Math.Min(basis.Length, 64 - tail.Length)` before appending so the final name never exceeds `64` characters.
   - **Rationale:** The original Option A included the host (or version) in the name so servers sharing a generic realm name stayed distinguishable. Since the card shows the host under the name, #98 drops it from the name; servers with the same realm name (or the same host and no realm) are still told apart by the `" #2"`, `" #3"` suffix and by the host and client shown on the card. Existing server names are never changed.

2. **`Cache/WDB` cleanup toggle in Settings — Approved: Option A (excluded from `1.0.0`, deferred to `1.1.0`).**
   - **Decision:** Do **not** include a `Cache/WDB` cleanup toggle (`CleanCacheOnSwitch`) in `1.0.0`; defer to `1.1.0`. In `1.0.0`, `StorageSettings(string BackupFolder)` exposes only `BackupFolder` (backup retention is not configurable through `StorageSettings` in `1.0.0`). Realm Portal does not touch or delete client files under `Cache/` or `WDB/` during normal client operations (unless a user explicitly points `BackupFolder` at one of those directories, in which case backup creation writes backup data there and retention deletes old Realm Portal backup directories).
   - **Rationale:** Realm Portal's core security and safety guarantee (verified across PR #65 and `docs/client-files.md`) is that it **only** touches the two small text configuration files (`realmlist.wtf` and `WTF/Config.wtf`) and that every file mutation is 100% byte-reversible via small (< 16 KiB) backups. Deleting `Cache/WDB` files is either irreversible or would require backing up megabytes of binary `.wdb` cache files (violating the small-backup invariant) and expands the filesystem deletion surface inside the user's game folder right before the `1.0.0` release.
