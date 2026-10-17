namespace RealmPortal.Core.Service;

/// <summary>
/// The only thing the UI talks to: all application logic (clients, servers, client files, active server, launch) sits behind it.
/// </summary>
/// <remarks>
/// <para><b>Synchronous.</b> Everything underneath is synchronous file and process work (applier, store, version reader), so the
/// service is too; the UI calls it with <c>Task.Run</c>. <see cref="IProgress{T}"/> callbacks run on the calling (worker) thread:
/// the UI marshals them itself.</para>
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
    ServiceResult<ClientEntry> AddClient(string folder);

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
    /// <see cref="ServiceErrorCode.LaunchFailed"/> with <see cref="Consistency.Unchanged"/> relative to the switch.
    /// </summary>
    ServiceResult<SwitchOutcome> Play(SwitchRequest request, IProgress<ServiceProgress>? progress = null);

    /// <summary>
    /// Undoes the last switch (one level): restores the client files of the client that was switched, byte for byte, and the previously
    /// active server, together. Client files changed since the switch are reported in <see cref="UndoOutcome.Files"/> and never overwritten.
    /// </summary>
    ServiceResult<UndoOutcome> Undo();

    // ------------------------------------------------------------ status and settings

    /// <summary>Active server, whether an undo is possible, and whether <c>activo.json</c> is in sync.</summary>
    ServiceResult<PortalStatus> GetStatus();

    /// <summary>The saved launcher settings (defaults when none were saved).</summary>
    ServiceResult<LauncherSettings> GetLauncherSettings();

    /// <summary>Validates and saves the launcher settings; an unusable combination fails with <see cref="ServiceErrorCode.InvalidLauncherSettings"/> and nothing is saved.</summary>
    ServiceResult<Unit> SetLauncherSettings(LauncherSettings settings);
}
