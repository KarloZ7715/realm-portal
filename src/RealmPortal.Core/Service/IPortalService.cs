using RealmPortal.Core.ClientFiles;

namespace RealmPortal.Core.Service;

/// <summary>
/// The only thing the UI talks to: all application logic (clients, servers, client files, active server, launch) sits behind it.
/// </summary>
/// <remarks>
/// <para><b>Synchronous.</b> Everything underneath is synchronous file and process work (applier, store, version reader), so the
/// service is too; the UI calls it with <c>Task.Run</c>. The service calls <see cref="IProgress{T}.Report"/> on the worker thread; where the handler runs
/// depends on the <see cref="IProgress{T}"/> implementation (for example <c>Progress&lt;T&gt;</c> posts to the <c>SynchronizationContext</c> captured at construction).</para>
/// <para><b>No exceptions.</b> Every failure is a <see cref="ServiceResult{T}"/> with a <see cref="ServiceError"/> (stable code +
/// parameters, no text). Only a programming error (null argument) throws.</para>
/// <para><b>No half states.</b> A multi-step operation either completes, or reports <see cref="ServiceError.Consistency"/>
/// <see cref="Consistency.Unchanged"/>/<see cref="Consistency.RolledBack"/>; <see cref="Consistency.Inconsistent"/> is the exceptional case
/// where even the rollback failed.</para>
/// </remarks>
public interface IPortalService
{
    // ------------------------------------------------------------ clients

    /// <summary>The registered client folders with their versions. A folder that cannot be read is an entry with <see cref="ClientEntry.Error"/>, not a failure.</summary>
    ServiceResult<IReadOnlyList<ClientEntry>> ListClients();

    /// <summary>Registers a client folder (the one with <c>Wow.exe</c>). Fails with the version-reading codes when it is not a readable client; adding the same folder twice returns the existing entry.</summary>
    ServiceResult<ClientEntry> AddClient(string folder, string? name = null);

    /// <summary>Discovers and registers known game installations and their configured servers.</summary>
    ServiceResult<ScanOutcome> Scan(ScanRequest? request = null, IProgress<ScanProgress>? progress = null);

    /// <summary>Updates the name and/or path of a registered client.</summary>
    ServiceResult<ClientEntry> UpdateClient(UpdateClientRequest request);

    /// <summary>Forgets a client folder (nothing on disk is touched). Backups of its installation are kept.</summary>
    ServiceResult<Unit> RemoveClient(string clientId);

    /// <summary>True while a game process that could be using the client is running ("game is open").</summary>
    ServiceResult<bool> IsGameRunning(string clientId);

    // ------------------------------------------------------------ servers

    /// <summary>
    /// All servers in the saved order. With a <paramref name="clientId"/>, <see cref="ServerEntry.IsCompatible"/> says which ones that
    /// client can play on (the UI shows the others as incompatible, or hides them with <paramref name="compatibleOnly"/>).
    /// </summary>
    ServiceResult<IReadOnlyList<ServerEntry>> ListServers(string? clientId = null, bool compatibleOnly = false);

    /// <summary>Adds a server by hand. There is no bundled list.</summary>
    ServiceResult<ServerEntry> AddServer(NewServer server);

    /// <summary>Updates a stored server without changing its id.</summary>
    ServiceResult<ServerEntry> UpdateServer(UpdateServerRequest request);

    /// <summary>Removes a server; when it was active it is deactivated and <c>activo.json</c> is deleted.</summary>
    ServiceResult<Unit> RemoveServer(string serverId);

    // ------------------------------------------------------------ switch, play, undo

    /// <summary>
    /// Makes a server active for a client: plan the client files, apply them (backup first), then mark the server active.
    /// Progress: <see cref="ServiceStep.Plan"/>, <see cref="ServiceStep.WriteFiles"/>, <see cref="ServiceStep.Activate"/>.
    /// </summary>
    ServiceResult<SwitchOutcome> SwitchServer(SwitchRequest request, IProgress<ServiceProgress>? progress = null);

    /// <summary>
    /// Switch, then start the game with the saved <see cref="LauncherSettings"/> ("Play"). Same steps as <see cref="SwitchServer"/> plus
    /// <see cref="ServiceStep.Launch"/>. If the launch fails, the switch stays done (the client is configured) and the error is
    /// <see cref="ServiceErrorCode.LaunchFailed"/> with <see cref="Consistency.AppliedLaunchFailed"/> (the switch is kept; refresh active server and undo from status).
    /// </summary>
    ServiceResult<SwitchOutcome> Play(SwitchRequest request, IProgress<ServiceProgress>? progress = null);

    /// <summary>
    /// Undoes the last switch (one level only): restores the client files of the client that was switched, byte for byte, and the previously
    /// active server, together. Nothing to undo: <see cref="ServiceErrorCode.NothingToUndo"/>. Client files that changed since the switch are
    /// never overwritten: the call fails with <see cref="ServiceErrorCode.UndoConflict"/> and <see cref="ServiceError.ConflictingFiles"/>, the
    /// active server stays and the undo remains pending, so the UI can offer to fix the files and retry, or <see cref="DiscardUndo"/>.
    /// </summary>
    ServiceResult<UndoOutcome> Undo(string? clientId = null);

    /// <summary>
    /// Gives up the pending undo (the "Discard" button of the conflict dialog): the change can no longer be undone from the app, the client files
    /// are not touched, the active server stays, and the next state is <see cref="ServiceErrorCode.NothingToUndo"/> (<c>CanUndo == false</c>). There is
    /// no "next pending backup": undo is single-level. The backup folder is left to the applier's retention.
    /// </summary>
    ServiceResult<PortalStatus> DiscardUndo(string? clientId = null);

    // ------------------------------------------------------------ backups

    /// <summary>Lists valid and damaged backups for a registered client, or across registered clients when null.</summary>
    ServiceResult<IReadOnlyList<BackupEntry>> ListBackups(string? clientId = null);

    /// <summary>Creates a manual snapshot for a registered client.</summary>
    ServiceResult<BackupEntry> CreateBackup(string clientId, string? label = null);

    /// <summary>Restores a named snapshot. Force creates a safety snapshot before overwriting changed files.</summary>
    ServiceResult<RestoreBackupOutcome> RestoreBackup(string clientId, string backupId, bool force = false);

    /// <summary>Deletes one snapshot from the backup store.</summary>
    ServiceResult<Unit> DeleteBackup(string clientId, string backupId);

    // ------------------------------------------------------------ status and settings

    /// <summary>Active server, whether an undo is possible, and whether <c>activo.json</c> is in sync.</summary>
    ServiceResult<PortalStatus> GetStatus();

    /// <summary>The saved launcher settings (defaults when none were saved).</summary>
    ServiceResult<LauncherSettings> GetLauncherSettings();

    /// <summary>The configured backup folder (what the Settings screen shows).</summary>
    ServiceResult<StorageSettings> GetStorageSettings();

    /// <summary>
    /// Validates and saves the backup folder (<see cref="ServiceErrorCode.PathNotFound"/>, <see cref="ServiceErrorCode.PermissionDenied"/>,
    /// <see cref="ServiceErrorCode.ReadOnlyFs"/>). Applies from the next start. The UI language is not stored by the service: the UI keeps its own file.
    /// </summary>
    ServiceResult<Unit> SetStorageSettings(StorageSettings settings);

    /// <summary>Validates and saves the launcher settings; an unusable combination fails with <see cref="ServiceErrorCode.InvalidLauncherSettings"/> and nothing is saved.</summary>
    ServiceResult<Unit> SetLauncherSettings(LauncherSettings settings);

    /// <summary>Update-check and status-probe settings (<c>network-settings.json</c>).</summary>
    ServiceResult<NetworkSettings> GetNetworkSettings();

    /// <summary>Saves update-check and status-probe settings. Both features default to on; see docs/updates-and-status.md.</summary>
    ServiceResult<Unit> SetNetworkSettings(NetworkSettings settings);
}

