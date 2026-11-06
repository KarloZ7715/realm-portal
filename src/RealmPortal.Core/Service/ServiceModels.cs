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

/// <summary>Optional parameters for client and server discovery.</summary>
public sealed record ScanRequest(bool ImportServers = true, IReadOnlyList<string>? AdditionalRoots = null);

/// <summary>Progress reported while the service scans candidate folders.</summary>
public sealed record ScanProgress(string CurrentSource, int DirectoriesInspected, int ClientsFound, int ServersImported);

/// <summary>Clients and servers registered by one completed discovery pass.</summary>
public sealed record ScanOutcome(
    IReadOnlyList<ClientEntry> AddedClients,
    IReadOnlyList<ServerEntry> ImportedServers,
    IReadOnlyList<ClientEntry> AllClients);

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
    bool IsCompatible,
    bool? ClearCacheOnSwitch = null);

/// <summary>Data of a server to add by hand.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Host">Realmlist host, e.g. <c>logon.example.org</c> or <c>10.0.0.5:3724</c>.</param>
/// <param name="Realm">Realm name to preselect in the login screen; <c>null</c> for none.</param>
/// <param name="PatchlistHost">Value of an optional <c>set patchlist</c> line; <c>null</c> for none.</param>
/// <param name="RequiredClientBuild">Client build the server needs (e.g. 12340); <c>null</c> for any.</param>
/// <param name="Account">Remembered account name (never a password); <c>null</c> for none.</param>
/// <param name="ClientId">Preferred client for this server (<see cref="ClientEntry.Id"/>), shown on its card; <c>null</c> for none. Unknown id: <see cref="ServiceErrorCode.ClientNotFound"/>.</param>
/// <param name="ExtraRealmlistLines">Extra realmlist lines after <c>set realmlist</c> (for example <c>set patchlist …</c>).</param>
/// <param name="ClearCacheOnSwitch">Per-server override for clearing Cache/WDB on switch; <c>null</c> uses the global Settings toggle.</param>
public sealed record NewServer(
    string Name,
    string Host,
    string? Realm = null,
    string? PatchlistHost = null,
    int? RequiredClientBuild = null,
    string? Account = null,
    string? ClientId = null,
    IReadOnlyList<string>? ExtraRealmlistLines = null,
    bool? ClearCacheOnSwitch = null);

/// <summary>Request to update an existing server.</summary>
/// <remarks>
/// <see cref="RequiredClientBuild"/> <c>null</c> keeps the stored build; <see cref="ClearRequiredClientBuild"/> <c>true</c>
/// removes it (the server then accepts any client; setting both is <see cref="ServiceErrorCode.InvalidServerInput"/> with
/// <c>Field = "build"</c>). <see cref="ExtraRealmlistLines"/> and
/// <see cref="PatchlistHost"/> both <c>null</c> keep the stored extra realmlist lines; an empty
/// <see cref="ExtraRealmlistLines"/> clears them. The other fields are always taken from the request
/// (including <see cref="ClearCacheOnSwitch"/>, which may be <c>null</c> to use the global Settings toggle).
/// </remarks>
public sealed record UpdateServerRequest(
    string ServerId,
    string Name,
    string Host,
    string? Realm = null,
    string? PatchlistHost = null,
    int? RequiredClientBuild = null,
    string? Account = null,
    string? ClientId = null,
    IReadOnlyList<string>? ExtraRealmlistLines = null,
    bool ClearRequiredClientBuild = false,
    bool? ClearCacheOnSwitch = null);

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
/// <param name="CacheClearWarning">When cache clear was attempted and failed soft, an English diagnostic; <c>null</c> when cleared successfully or skipped.</param>
public sealed record SwitchOutcome(
    ServerEntry Active,
    ServerEntry? Previous,
    string? BackupId,
    IReadOnlyList<string> ChangedFiles,
    string? CacheClearWarning = null);

/// <summary>
/// Result of a completed undo: the client files are back to what they were before the switch and the previous server is active again.
/// Client files that changed after the switch are not a success: see <see cref="ServiceErrorCode.UndoConflict"/>.
/// </summary>
/// <param name="Active">The server that is active after the undo; <c>null</c> when none is.</param>
/// <param name="Files">What happened to each client file.</param>
/// <param name="ClientFilesNoticeLines">English lines for the CLI when undo touched (or skipped) registered client folders, one entry per output line.</param>
public sealed record UndoOutcome(
    ServerEntry? Active,
    IReadOnlyList<UndoFileResult> Files,
    IReadOnlyList<string>? ClientFilesNoticeLines = null);

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
    IReadOnlyList<ClientEntry> ClientsNotMatchingActive,
    IReadOnlyDictionary<string, ServerEntry>? ActiveByClient = null);

/// <summary>Where backups are kept and whether Cache/WDB is cleared on switch, set from the Settings screen.</summary>
/// <param name="BackupFolder">Folder for backups. A change applies from the next start of the app (<see cref="PortalStatus.BackupFolder"/> shows the one in use); existing backups are not moved or deleted.</param>
/// <param name="ClearCacheOnSwitch">When true, a successful switch to another server clears the client's <c>Cache</c> and <c>WDB</c> folders (off by default). A per-server override on <see cref="Servers.Server.ClearCacheOnSwitch"/> wins when set.</param>
public sealed record StorageSettings(string BackupFolder, bool ClearCacheOnSwitch = false);

/// <summary>
/// Update-check and status-probe settings persisted in <c>network-settings.json</c>
/// (docs/updates-and-status.md). Defaults match the product decision: both features on.
/// </summary>
/// <param name="UpdatesAutoCheck">Daily automatic GitHub release check; false means zero automatic network requests.</param>
/// <param name="UpdatesLastAttemptUtc">Last automatic check attempt (success or failure); future values are treated as now by the checker.</param>
/// <param name="UpdatesEtag">ETag from the last 200 releases/latest response.</param>
/// <param name="UpdatesSkippedVersion">Version the user chose to skip.</param>
/// <param name="UpdatesLaterVersion">Version postponed with Later.</param>
/// <param name="UpdatesLaterUntilUtc">Do not offer <paramref name="UpdatesLaterVersion"/> again before this time.</param>
/// <param name="StatusProbeEnabled">Server status dot; false hides the dot and schedules no probes.</param>
public sealed record NetworkSettings(
    bool UpdatesAutoCheck = true,
    DateTimeOffset? UpdatesLastAttemptUtc = null,
    string? UpdatesEtag = null,
    string? UpdatesSkippedVersion = null,
    string? UpdatesLaterVersion = null,
    DateTimeOffset? UpdatesLaterUntilUtc = null,
    bool StatusProbeEnabled = true)
{
    public static NetworkSettings Default { get; } = new();
}

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

/// <summary>A config store file that keeps a <c>.bak</c> of its previous valid version (#196).</summary>
public enum ConfigFileKind
{
    /// <summary><c>servers.json</c>.</summary>
    Servers,

    /// <summary><c>clients.json</c>.</summary>
    Clients,
}

/// <summary>The state of a config file or of its <c>.bak</c>.</summary>
public enum ConfigFileState
{
    Missing,
    Valid,

    /// <summary>Present but unreadable or not a valid store file.</summary>
    Damaged,
}

/// <summary>A config store file and its <c>.bak</c> (see <see cref="IPortalService.GetConfigBackups"/>).</summary>
/// <param name="BackupModifiedAt">Last write time of the <c>.bak</c>, or <c>null</c> when it is missing.</param>
public sealed record ConfigBackupStatus(
    ConfigFileKind File,
    string Path,
    ConfigFileState Current,
    string BackupPath,
    ConfigFileState Backup,
    DateTimeOffset? BackupModifiedAt);
