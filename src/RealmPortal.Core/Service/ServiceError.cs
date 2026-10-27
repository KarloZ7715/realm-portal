using System.Diagnostics.CodeAnalysis;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

/// <summary>
/// Stable, machine-readable reason why a service call failed. The UI maps each code (plus the parameters in
/// <see cref="ServiceError"/>) to localized text; Core never produces user-facing text.
/// </summary>
public enum ServiceErrorCode
{
    // ---- client files (names match ClientFilesErrorCode one to one) ----
    /// <summary>The game is running; client files must not be changed.</summary>
    GameRunning,

    /// <summary>The running game processes could not be listed, so "not running" cannot be confirmed.</summary>
    ProcessCheckFailed,

    PermissionDenied,
    ReadOnlyFs,

    /// <summary>Not enough space to complete the write.</summary>
    DiskFull,

    /// <summary>The file is in use by another process.</summary>
    FileSharingViolation,

    /// <summary>The client directory or a path inside it does not exist.</summary>
    PathNotFound,

    /// <summary>A path is outside the managed client-file allow-list (apply/undo refuse to touch it).</summary>
    PathEscapesClient,

    /// <summary>A client file changed since it was planned or backed up (user or game).</summary>
    HashMismatch,

    VerifyFailed,

    /// <summary>The client version has no known file layout; <see cref="ServiceError.ClientVersion"/> says which version was found.</summary>
    UnsupportedClient,

    /// <summary>The server's realmlist breaks the contract; see <see cref="ServiceError.RealmlistKind"/>, <see cref="ServiceError.LineNumber"/>, <see cref="ServiceError.ServerName"/>.</summary>
    InvalidRealmlist,

    /// <summary>Nothing was applied that could be undone (also the state after <see cref="IPortalService.DiscardUndo"/>).</summary>
    NothingToUndo,

    /// <summary>
    /// Undo found client files that changed after the switch. They were not overwritten; <see cref="ServiceError.ConflictingFiles"/> lists them.
    /// The undo stays pending (<see cref="Consistency.Pending"/>): fix the files and call <c>Undo</c> again, or <see cref="IPortalService.DiscardUndo"/>.
    /// </summary>
    UndoConflict,

    // ---- reading a client's version ----
    /// <summary>The folder has no <c>Wow.exe</c>.</summary>
    ClientExecutableNotFound,
    ClientNotAPortableExecutable,
    ClientNoVersionResource,

    /// <summary>The client executable exists but cannot be read.</summary>
    ClientUnreadable,

    // ---- servers, clients and settings ----
    /// <summary>No server with that id.</summary>
    ServerNotFound,

    /// <summary>No registered client with that id.</summary>
    ClientNotFound,

    /// <summary>The requested backup is missing or has an invalid id.</summary>
    BackupNotFound,

    /// <summary>A client with the requested canonical folder path is already registered.</summary>
    ClientAlreadyExists,

    /// <summary>A client name is invalid.</summary>
    InvalidClientInput,

    /// <summary>Another server already has that name (case-insensitive); <see cref="ServiceError.ServerName"/> is set.</summary>
    ServerNameTaken,

    /// <summary>A field of a new server is invalid; <see cref="ServiceError.Field"/> names it (<c>name</c>, <c>host</c>, <c>realm</c>, <c>account</c>, <c>build</c>).</summary>
    InvalidServerInput,

    /// <summary>The server needs another client build; <see cref="ServiceError.RequiredBuild"/> and <see cref="ServiceError.ClientBuild"/> are set.</summary>
    IncompatibleClient,

    /// <summary>servers.json, state.json or the client list is unreadable or has an unsupported format (never overwritten).</summary>
    StoreCorrupt,

    /// <summary>Another Realm Portal process still holds <c>.realm-portal.lock</c>; the operation made no change and can be retried.</summary>
    StoreLocked,

    /// <summary>Undo cannot restore the previous server because it was removed.</summary>
    UndoTargetMissing,

    /// <summary>The launcher settings cannot be resolved into a command (for example the template has no <c>{exe}</c>).</summary>
    InvalidLauncherSettings,

    /// <summary>The game process could not be started.</summary>
    LaunchFailed,

    // ---- updates (#105) ----
    /// <summary>The GitHub Releases check failed (network, HTTP status, parse).</summary>
    UpdateCheckFailed,

    /// <summary>Downloaded bytes did not match the release SHA256SUMS digest.</summary>
    ChecksumMismatch,

    /// <summary>The expected release asset or its SHA256SUMS line is missing or duplicated.</summary>
    AssetMissing,

    /// <summary>A download exceeded the pinned size or the hard 300 MB / 1 MiB cap.</summary>
    DownloadTooLarge,

    /// <summary>A URL was outside the HTTPS allowlist (host / scheme / port / userinfo).</summary>
    UntrustedRedirect,

    /// <summary>GitHub returned 403/429; wait for Retry-After before trying again.</summary>
    RateLimited,

    // ---- cross-cutting ----
    /// <summary>A multi-step operation failed and the compensating step failed too: the app state and the client files may disagree. See <see cref="ServiceError.Consistency"/>.</summary>
    InconsistentState,

    /// <summary>Anything not listed above; <see cref="ServiceError.Diagnostic"/> carries the exception text for logs.</summary>
    Unexpected,
}

/// <summary>What state the operation left behind when it failed.</summary>
public enum Consistency
{
    /// <summary>Nothing was changed.</summary>
    Unchanged,

    /// <summary>Some steps ran, then everything was put back (the app and the client are as before the call).</summary>
    RolledBack,

    /// <summary>The operation could not be rolled back; the user should be told to review the client.</summary>
    Inconsistent,

    /// <summary>
    /// The operation stopped on purpose and is still pending (only <see cref="ServiceErrorCode.UndoConflict"/>): some client files may already be
    /// restored, the active server did not change, and the undo can be finished or discarded.
    /// </summary>
    Pending,

    /// <summary>
    /// Only <see cref="IPortalService.Play"/>: plan, apply and activate succeeded but launch failed. The client is configured, the new server is active,
    /// and undo is still available. Pair with <see cref="ServiceErrorCode.LaunchFailed"/>.
    /// </summary>
    AppliedLaunchFailed,
}

/// <summary>A failure as data: a stable code plus the parameters the UI needs for its message. Everything except <see cref="Code"/> is optional.</summary>
/// <param name="Code">What went wrong.</param>
/// <param name="ServerName">Server involved (name clash, invalid realmlist, ...).</param>
/// <param name="Path">File or folder involved.</param>
/// <param name="LineNumber">1-based realmlist line for <see cref="ServiceErrorCode.InvalidRealmlist"/>.</param>
/// <param name="RealmlistKind">Why the realmlist is invalid (<see cref="ServiceErrorCode.InvalidRealmlist"/>).</param>
/// <param name="Field">Offending field of <see cref="NewServer"/> (<see cref="ServiceErrorCode.InvalidServerInput"/>).</param>
/// <param name="RequiredBuild">Build the server needs (<see cref="ServiceErrorCode.IncompatibleClient"/>).</param>
/// <param name="ClientBuild">Build of the selected client (<see cref="ServiceErrorCode.IncompatibleClient"/>).</param>
/// <param name="ClientVersion">Version found in the client (file version, e.g. <c>6.2.4.20779</c>) for <see cref="ServiceErrorCode.UnsupportedClient"/>.</param>
/// <param name="ConflictingFiles">Client files that differ from what the switch wrote, relative to the client folder with <c>/</c> separators (<see cref="ServiceErrorCode.UndoConflict"/>).</param>
/// <param name="Consistency">What the failed operation left behind.</param>
/// <param name="Diagnostic">English exception text for logs. Not UI text and not stable.</param>
public sealed record ServiceError(
    ServiceErrorCode Code,
    string? ServerName = null,
    string? Path = null,
    int? LineNumber = null,
    InvalidRealmlistKind? RealmlistKind = null,
    string? Field = null,
    int? RequiredBuild = null,
    int? ClientBuild = null,
    string? ClientVersion = null,
    IReadOnlyList<string>? ConflictingFiles = null,
    Consistency Consistency = Consistency.Unchanged,
    string Diagnostic = "");

/// <summary>Nothing to return.</summary>
public readonly record struct Unit;

/// <summary>Outcome of a service call: a value or an error, never an exception.</summary>
public sealed record ServiceResult<T>
{
    private ServiceResult(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ServiceError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Error is null;

    public static ServiceResult<T> Ok(T value) => new(value, null);

    public static ServiceResult<T> Fail(ServiceError error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));
}
