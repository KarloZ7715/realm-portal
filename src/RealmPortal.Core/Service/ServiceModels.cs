using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;

namespace RealmPortal.Core.Service;

// ---------------------------------------------------------------- clients

/// <summary>A game client folder known to the app.</summary>
/// <param name="Id">Stable id derived from the folder path (letters and digits, 17 chars); also the installation id of the backups.</param>
/// <param name="Path">The client folder (the one with <c>Wow.exe</c>).</param>
/// <param name="Version">The client version; <c>null</c> when it could not be read (then <see cref="Error"/> says why).</param>
/// <param name="Error">Why the version could not be read (<c>PathNotFound</c>, <c>ClientExecutableNotFound</c>, <c>ClientNotAPortableExecutable</c>, ...). An unreadable client is a normal list entry, not a failure of the call.</param>
public sealed record ClientEntry(
    string Id,
    string Path,
    ClientVersion? Version,
    ServiceError? Error,
    string? Name = null,
    ClientOrigin Origin = ClientOrigin.Manual);

/// <summary>Request to update a registered client.</summary>
public sealed record UpdateClientRequest(string ClientId, string? Name = null, string? Folder = null);

// ---------------------------------------------------------------- servers

/// <summary>A server as the UI shows it.</summary>
/// <param name="ClientId">The client the server was assigned to when it was added (<see cref="NewServer.ClientId"/>); <c>null</c> when none, or when that client was removed.</param>
/// <param name="IsActive">True for the active server.</param>
/// <param name="IsCompatible">True when the selected client can play on it; always true when no client was selected.</param>
public sealed record ServerEntry(
    string Id,
    string Name,
    string Host,
    string Realm,
    string Realmlist,
    string? Account,
    int? RequiredClientBuild,
    string? ClientId,
    bool IsActive,
    bool IsCompatible);

/// <summary>Data of a server to add by hand.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Host">Realmlist host, e.g. <c>logon.example.org</c> or <c>10.0.0.5:3724</c>.</param>
/// <param name="Realm">Realm name to preselect in the login screen; <c>null</c> for none.</param>
/// <param name="PatchlistHost">Value of an optional <c>set patchlist</c> line; <c>null</c> for none.</param>
/// <param name="RequiredClientBuild">Client build the server needs (e.g. 12340); <c>null</c> for any.</param>
/// <param name="Account">Remembered account name (never a password); <c>null</c> for none.</param>
/// <param name="ClientId">Preferred client for this server (<see cref="ClientEntry.Id"/>), shown on its card; <c>null</c> for none. Unknown id: <see cref="ServiceErrorCode.ClientNotFound"/>.</param>
/// <param name="ExtraRealmlistLines">Extra realmlist lines after <c>set realmlist</c> (for example <c>set patchlist …</c>).</param>
public sealed record NewServer(
    string Name,
    string Host,
    string? Realm = null,
    string? PatchlistHost = null,
    int? RequiredClientBuild = null,
    string? Account = null,
    string? ClientId = null,
    IReadOnlyList<string>? ExtraRealmlistLines = null);

/// <summary>Request to update an existing server.</summary>
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

// ---------------------------------------------------------------- switch, play, undo

/// <summary>The parts of a switch or play, in the order they run. Reported through <see cref="ServiceProgress"/>.</summary>
public enum ServiceStep
{
    /// <summary>Dry run: work out which client files change. Writes nothing.</summary>
    Plan,

    /// <summary>Back up the client files, then write <c>realmlist.wtf</c>/<c>Config.wtf</c> (one indivisible call of the applier).</summary>
    WriteFiles,

    /// <summary>Make the server the active one (<c>state.json</c> and <c>activo.json</c>).</summary>
    Activate,

    /// <summary>Start the game (play only).</summary>
    Launch,
}

public enum StepState
{
    Started,
    Completed,
    Failed,
}

/// <summary>A progress event. Each step reports <see cref="StepState.Started"/> and then Completed or Failed (with the error).</summary>
public sealed record ServiceProgress(ServiceStep Step, StepState State, ServiceError? Error = null);

/// <summary>Switch or play request.</summary>
/// <param name="ClientId">Which client to configure/start (<see cref="ClientEntry.Id"/>).</param>
/// <param name="ServerId">Which server to switch to.</param>
/// <param name="Force">Switch even if the client does not match the server's required build ("Switch anyway").</param>
public sealed record SwitchRequest(string ClientId, string ServerId, bool Force = false);

/// <summary>Result of a successful switch or play.</summary>
/// <param name="Active">The server that is now active.</param>
/// <param name="Previous">The server that was active before; <c>null</c> when none was.</param>
/// <param name="BackupId">Backup made for this switch; <c>null</c> when the client already had this configuration (nothing written).</param>
/// <param name="ChangedFiles">Client files written, relative to the client folder, with <c>/</c> separators.</param>
public sealed record SwitchOutcome(
    ServerEntry Active,
    ServerEntry? Previous,
    string? BackupId,
    IReadOnlyList<string> ChangedFiles);

/// <summary>
/// Result of a completed undo: the client files are back to what they were before the switch and the previous server is active again.
/// Client files that changed after the switch are not a success: see <see cref="ServiceErrorCode.UndoConflict"/>.
/// </summary>
/// <param name="Active">The server that is active after the undo; <c>null</c> when none is.</param>
/// <param name="Files">What happened to each client file.</param>
/// <param name="ClientFilesNotice">English line for the CLI when undo touched (or skipped) registered client folders.</param>
public sealed record UndoOutcome(
    ServerEntry? Active,
    IReadOnlyList<UndoFileResult> Files,
    string? ClientFilesNotice = null);

// ---------------------------------------------------------------- status and settings

/// <summary>Overall state for the status bar and screens.</summary>
/// <param name="Active">The active server; <c>null</c> when none.</param>
/// <param name="CanUndo">True when <see cref="IPortalService.Undo"/> has something to undo.</param>
/// <param name="ProjectionInSync">False when <c>activo.json</c> does not match the active server (edited or deleted by hand).</param>
/// <param name="BackupFolder">The folder where client-file backups are written right now.</param>
/// <param name="ClientsNotMatchingActive">Registered clients whose on-disk realmlist does not match the active server (empty when none active).</param>
public sealed record PortalStatus(
    ServerEntry? Active,
    bool CanUndo,
    bool ProjectionInSync,
    string BackupFolder,
    IReadOnlyList<ClientEntry> ClientsNotMatchingActive);

/// <summary>Where backups are kept, set from the Settings screen.</summary>
/// <param name="BackupFolder">Folder for backups. A change applies from the next start of the app (<see cref="PortalStatus.BackupFolder"/> shows the one in use); existing backups are not moved or deleted.</param>
public sealed record StorageSettings(string BackupFolder);

/// <summary>
/// How to start the game, set from the Settings screen. Everything of a <see cref="GameLaunchSpec"/> except the
/// executable path and working directory, which come from the selected client.
/// </summary>
/// <param name="CommandTemplate">Optional command such as <c>wine {exe}</c> (see docs/launch.md).</param>
/// <param name="Arguments">Extra arguments.</param>
/// <param name="Environment">Environment variables; a <c>null</c> value removes the variable.</param>
/// <param name="Prefix">Wrapper tokens that go first, e.g. <c>["gamemoderun"]</c>.</param>
public sealed record LauncherSettings(
    string? CommandTemplate = null,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    IReadOnlyList<string>? Prefix = null)
{
    /// <summary>Completes the settings with the client's executable and folder.</summary>
    public GameLaunchSpec ToSpec(string executablePath, string workingDirectory) =>
        new(executablePath, workingDirectory, CommandTemplate, Arguments, Environment, Prefix);
}
