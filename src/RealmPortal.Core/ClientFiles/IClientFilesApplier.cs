namespace RealmPortal.Core.ClientFiles;

/// <summary>Outcome of <see cref="IClientFilesApplier.Apply"/>.</summary>
/// <param name="BackupId">Id of the backup that <see cref="IClientFilesApplier.Undo"/> restores; <c>null</c> when the plan was empty.</param>
/// <param name="Edits">The edits that were written.</param>
public sealed record ApplyResult(string? BackupId, IReadOnlyList<ClientFileEdit> Edits, IReadOnlyList<string> Warnings)
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
    /// Undoes the most recent applied change of the installation that has not been undone yet. Files whose current
    /// content is not what the apply wrote are reported as conflicts and never overwritten.
    /// </summary>
    /// <param name="clientRoot">The installation's trusted client directory (must match the backup manifest).</param>
    /// <exception cref="ClientFilesException"><see cref="ClientFilesErrorCode.NothingToUndo"/> when no change is pending.</exception>
    UndoResult Undo(string installationId, string clientRoot);
}
