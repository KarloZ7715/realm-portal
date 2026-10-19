namespace RealmPortal.Core.ClientFiles;

/// <summary>Outcome of <see cref="IClientFilesApplier.Apply"/>.</summary>
/// <param name="BackupId">Id of the backup that <see cref="IClientFilesApplier.Undo"/> restores; <c>null</c> when the plan was empty.</param>
/// <param name="Edits">The edits that were written.</param>
/// <param name="ManifestSha256">SHA-256 of the <c>manifest.json</c> bytes that were written; <c>null</c> when no backup was made.</param>
public sealed record ApplyResult(
    string? BackupId,
    IReadOnlyList<ClientFileEdit> Edits,
    IReadOnlyList<string> Warnings,
    string? ManifestSha256 = null)
{
    public ApplyResult(string? backupId, IReadOnlyList<ClientFileEdit> edits)
        : this(backupId, edits, [])
    {
    }
}

public enum UndoFileStatus
{
    /// <summary>The original bytes were restored.</summary>
    Restored,

    /// <summary>The file did not exist before the change; it was deleted (folders are never removed).</summary>
    Deleted,

    /// <summary>The file already had its original state (for example a previous undo was interrupted).</summary>
    AlreadyRestored,

    /// <summary>The file changed since the apply (user or game); it was left untouched for the UI to decide.</summary>
    Conflict,
}

/// <param name="RelativePath">Same format as <see cref="ClientFileEdit.RelativePath"/>.</param>
public sealed record UndoFileResult(string RelativePath, UndoFileStatus Status);

/// <param name="BackupId">The backup that was processed.</param>
/// <param name="Files">One entry per file of the backup.</param>
public sealed record UndoResult(string BackupId, IReadOnlyList<UndoFileResult> Files)
{
    /// <summary>True when at least one file was not restored because it changed after the apply.</summary>
    public bool HasConflicts => Files.Any(f => f.Status == UndoFileStatus.Conflict);
}

/// <summary>Metadata for a retained client configuration snapshot.</summary>
public sealed record BackupEntry(
    string BackupId, string ClientId, string ClientPath, string ServerId,
    DateTimeOffset CreatedAt, long TotalBytes, IReadOnlyList<string> Files,
    DateTimeOffset? UndoneAt = null, bool IsManual = false, string? Label = null,
    bool WasDeduplicated = false);

/// <summary>Outcome of restoring a named backup.</summary>
public sealed record RestoreBackupOutcome(string RestoredBackupId, string? SafetyBackupId, IReadOnlyList<UndoFileResult> Files);

/// <summary>
/// Writes a server's realm address into a game client's files with a backup first, and undoes it byte-for-byte.
/// Failures are <see cref="ClientFilesException"/> with a <see cref="ClientFilesErrorCode"/>.
/// </summary>
public interface IClientFilesApplier
{
    /// <summary>Dry run: computes the exact edits for the request without writing anything (the backup root is not touched either).</summary>
    /// <exception cref="ClientFilesException">Unsupported client, invalid realmlist, missing client directory or unreadable file.</exception>
    ClientFilesPlan Plan(ClientFilesRequest request);

    /// <summary>
    /// Applies a plan: refuses while the game runs, writes a backup and manifest first, then each file atomically,
    /// verifies the result and reverts everything on failure. A file that changed since planning fails with
    /// <see cref="ClientFilesErrorCode.HashMismatch"/> (plan again to refresh).
    /// </summary>
    ApplyResult Apply(ClientFilesPlan plan);

    /// <summary>
    /// Applies a plan while an installation lock from <see cref="AcquireInstallationLock"/> is already held
    /// (the lock is not released until the caller disposes it).
    /// </summary>
    ApplyResult Apply(ClientFilesPlan plan, InstallationLock installationLock);

    /// <summary>Acquires the cross-process installation lock; dispose it when apply/activate/rollback is finished.</summary>
    InstallationLock AcquireInstallationLock(string installationId);

    /// <summary>
    /// Undoes the most recent applied change of the installation that has not been undone yet. Files whose current
    /// content is not what the apply wrote are reported as conflicts and never overwritten.
    /// </summary>
    /// <summary>
    /// Undoes a specific pending backup (or the newest pending one when <paramref name="backupId"/> is null).
    /// <paramref name="clientRoot"/> is the trusted client folder from <see cref="Clients.ClientStore"/> (must match the backup manifest).
    /// When <paramref name="markUndone"/> is false, restored files stay pending in the manifest until <see cref="MarkBackupUndone"/>.
    /// </summary>
    /// <param name="clientRoot">The installation's trusted client directory (must match the backup manifest).</param>
    /// <param name="expectedManifestSha256">
    /// SHA-256 of the manifest bytes, kept outside the backup folder. When set, the manifest just read must hash to
    /// this value or Undo throws <see cref="ClientFilesErrorCode.HashMismatch"/> before it changes any client file.
    /// <c>null</c> skips the check, which is how a journal written before <c>manifest_sha256</c> existed still undoes.
    /// </param>
    /// <exception cref="ClientFilesException"><see cref="ClientFilesErrorCode.NothingToUndo"/> when no change is pending.</exception>
    UndoResult Undo(
        string installationId,
        string clientRoot,
        string? backupId = null,
        bool markUndone = true,
        InstallationLock? installationLock = null,
        string? expectedManifestSha256 = null);

    /// <summary>
    /// Marks a backup as fully undone after the application state undo has been committed.
    /// A backup that is already marked undone, or whose manifest is already gone, is left as it is.
    /// </summary>
    void MarkBackupUndone(string installationId, string backupId);

    IReadOnlyList<BackupEntry> ListBackups(string? installationId = null);
    BackupEntry CreateBackup(string installationId, string clientRoot, string? label = null);
    RestoreBackupOutcome RestoreBackup(string installationId, string clientRoot, string backupId, bool force = false);
    void DeleteBackup(string installationId, string backupId);
    void PruneBackups(string? installationId = null, int? keepBackups = null);
}
