using System.Security.Cryptography;
using System.Text;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.ClientFiles;

/// <summary>
/// Backup-first apply and byte-identical undo of client file edits. Layout of a backup (see docs/client-files-backup.md):
/// <c>&lt;backupRoot&gt;/&lt;installationId&gt;/&lt;backupId&gt;/manifest.json</c> plus the original bytes in <c>files/&lt;n&gt;</c>.
/// </summary>
public sealed class ClientFilesApplier : IClientFilesApplier
{
    public const string BackupDirectoryVariable = "REALM_PORTAL_BACKUP_DIR";
    private const string ManifestName = "manifest.json";

    /// <summary>How many backups are kept per installation by default.</summary>
    public const int DefaultKeepBackups = 10;

    /// <summary>
    /// A <c>files/&lt;n&gt;</c> payload larger than this is refused before it is read.
    /// Realmlist and Config.wtf backups are a few kilobytes; the cap stops a swapped-in file from being loaded whole.
    /// </summary>
    internal const int MaxBackupFileBytes = 16 * 1024 * 1024;

    private readonly IClientFilesPlanner _planner;
    private readonly IAtomicFileWriter _writer;
    private readonly IGameProcessProbe _probe;
    private readonly string _backupRoot;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _lockTimeout;
    private readonly int _keepBackups;

    public ClientFilesApplier(
        IClientFilesPlanner planner,
        IAtomicFileWriter writer,
        IGameProcessProbe probe,
        string backupRoot,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? lockTimeout = null,
        int keepBackups = DefaultKeepBackups)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        _planner = planner;
        _writer = writer;
        _probe = probe;
        _backupRoot = Path.GetFullPath(backupRoot);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lockTimeout = lockTimeout ?? TimeSpan.FromSeconds(10);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepBackups, 1);
        _keepBackups = keepBackups;
    }

    /// <summary>Where backups live by default: <c>$REALM_PORTAL_BACKUP_DIR</c>, else <c>realm-portal/backups</c> under the platform local app-data folder.</summary>
    public static string DefaultBackupRoot(Func<string, string?>? getVariable = null)
    {
        var overridden = (getVariable ?? Environment.GetEnvironmentVariable)(BackupDirectoryVariable);
        return !string.IsNullOrWhiteSpace(overridden)
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "realm-portal", "backups");
    }

    public ClientFilesPlan Plan(ClientFilesRequest request) => _planner.Plan(request);

    public InstallationLock AcquireInstallationLock(string installationId)
    {
        RequireValidIds(installationId, "-");
        return new InstallationLock(AcquireLock(installationId), installationId);
    }

    public ApplyResult Apply(ClientFilesPlan plan)
    {
        using var installationLock = AcquireInstallationLock(plan.InstallationId);
        return Apply(plan, installationLock);
    }

    public ApplyResult Apply(ClientFilesPlan plan, InstallationLock installationLock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(installationLock);
        if (!string.Equals(plan.InstallationId, installationLock.InstallationId, StringComparison.Ordinal))
            throw new ArgumentException("The installation lock does not match the plan.", nameof(installationLock));

        RequireValidIds(plan.InstallationId, plan.ProfileId);
        if (plan.IsEmpty)
            return new ApplyResult(null, []);

        var clientRoot = ClientPaths.CanonicalRoot(plan.ClientRoot);
        if (_probe.IsGameRunning(clientRoot))
            throw new ClientFilesException(ClientFilesErrorCode.GameRunning, clientRoot, "The game is running; close it before changing its files.");

        // Read and verify the current state of every file before touching anything.
        var targets = new List<Target>();
        var warnings = new List<string>();
        foreach (var edit in plan.Edits)
        {
            var path = ClientPaths.ResolveManagedPath(clientRoot, edit.RelativePath);
            var original = ReadIfExists(path);
            var currentHash = original is null ? null : ClientFilesIo.Sha256Hex(original);
            if (!string.Equals(currentHash, edit.Sha256Before, StringComparison.Ordinal))
            {
                throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path,
                    $"'{path}' changed since the plan was made; plan again.");
            }

            var permissions = FilePermissionSnapshot.Capture(path);
            if (permissions is not null && FilePermissionSnapshot.IsReadOnly(path))
                warnings.Add($"'{edit.RelativePath}' was read-only; its permissions were preserved.");

            targets.Add(new Target(edit, path, original, permissions, SymlinkSnapshot.Capture(path)));
        }

        var backupId = NewBackupId(plan.InstallationId);
        var backupDir = Path.Combine(_backupRoot, plan.InstallationId, backupId);
        string manifestSha256;
        try
        {
            manifestSha256 = WriteBackup(plan, clientRoot, backupId, backupDir, targets);
        }
        catch
        {
            TryDeleteDirectory(backupDir); // the client was not touched yet
            throw;
        }

        var attempted = new List<Target>();
        try
        {
            foreach (var target in targets)
            {
                attempted.Add(target);
                Guard(() => _writer.WriteAllBytes(target.Path, target.Edit.NewBytes, target.Permissions?.ToReplacementPermissions()), target.Path, "write");
                var written = Guard(() => File.ReadAllBytes(target.Path), target.Path, "read back");
                if (ClientFilesIo.Sha256Hex(written) != target.Edit.Sha256After)
                    throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, target.Path, $"'{target.Path}' does not hold the expected bytes after writing.");
            }
        }
        catch
        {
            if (Revert(attempted))
                TryDeleteDirectory(backupDir); // nothing changed, so there is nothing to undo
            // else: the backup stays so that Undo can finish the revert.
            throw;
        }

        Prune(plan.InstallationId);
        return new ApplyResult(backupId, plan.Edits, warnings, manifestSha256);
    }

    public UndoResult Undo(
        string installationId,
        string clientRoot,
        string? backupId = null,
        bool markUndone = true,
        InstallationLock? installationLock = null,
        string? expectedManifestSha256 = null)
    {
        if (installationLock is not null && !string.Equals(installationId, installationLock.InstallationId, StringComparison.Ordinal))
            throw new ArgumentException("The installation lock does not match the installation.", nameof(installationLock));

        RequireValidIds(installationId, "-");
        ArgumentException.ThrowIfNullOrWhiteSpace(clientRoot);
        var trustedRoot = ClientPaths.CanonicalRoot(clientRoot);
        if (backupId is not null && !BackupIdFormat.IsValid(backupId))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, clientRoot, $"Backup id '{backupId}' is not valid.");

        var ownsLock = installationLock is null;
        installationLock ??= AcquireInstallationLock(installationId);
        try
        {
            var (manifestPath, manifest, manifestBytes) = backupId is null
                ? FindPending(installationId)
                  ?? throw new ClientFilesException(ClientFilesErrorCode.NothingToUndo, Path.Combine(_backupRoot, installationId),
                      $"There is no applied change to undo for '{installationId}'.")
                : FindBackup(installationId, backupId)
                  ?? throw new ClientFilesException(ClientFilesErrorCode.NothingToUndo, Path.Combine(_backupRoot, installationId),
                      $"There is no pending backup '{backupId}' to undo for '{installationId}'.");

            if (backupId is not null && !string.Equals(manifest.BackupId, backupId, StringComparison.Ordinal))
                throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, clientRoot, "The pending backup id does not match the requested rollback.");

            ClientPaths.RequireMatchingRoots(trustedRoot, manifest.ClientRoot, manifestPath);

            // manifest.json sits in the backup folder, so its bytes are not a trust anchor. A hash kept outside
            // that folder (the last-switch journal) rejects a rewritten manifest before any client file changes.
            // No hash means a journal written before manifest_sha256 existed; those undos are not checked.
            if (expectedManifestSha256 is not null
                && !string.Equals(ClientFilesIo.Sha256Hex(manifestBytes), expectedManifestSha256, StringComparison.Ordinal))
            {
                throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, manifestPath,
                    $"The backup manifest '{manifestPath}' was modified; it cannot be restored safely.");
            }

            if (_probe.IsGameRunning(trustedRoot))
                throw new ClientFilesException(ClientFilesErrorCode.GameRunning, trustedRoot, "The game is running; close it before restoring its files.");

            var backupDir = Path.GetDirectoryName(manifestPath)!;

            // Resolve, hash-check and read every entry before restoring anything so a bad entry cannot leave a partial undo.
            var resolved = manifest.Files
                .Select(file => (file, path: ClientPaths.ResolveManagedPath(trustedRoot, file.Path)))
                .ToList();
            BackupManifest.EnsureBackupFilesReferenced(backupDir, manifest.Files);

            var prepared = new List<PreparedUndo>(resolved.Count);
            foreach (var (file, path) in resolved)
            {
                byte[]? original = null;
                string? linkText = null;
                if (file.Existed)
                {
                    original = ReadBackupFile(backupDir, file);
                    linkText = ReadBoundSidecar(backupDir, file);
                }

                prepared.Add(new PreparedUndo(file, path, original, linkText));
            }

            var results = new List<UndoFileResult>();
            foreach (var item in prepared)
            {
                var file = item.File;
                var path = item.Path;
                // ReadIfExists stays for other callers. This read follows a symlink (the managed path may be one)
                // but must not block on a FIFO or drain a device such as /dev/zero.
                byte[]? current;
                bool currentNotComparable;
                try
                {
                    current = ReadCurrentForUndo(path, out currentNotComparable);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw ClientFilesIo.Map(ex, path, "read");
                }

                var currentHash = currentNotComparable || current is null ? null : ClientFilesIo.Sha256Hex(current);

                UndoFileStatus status;
                if (currentNotComparable)
                {
                    status = UndoFileStatus.Conflict;
                }
                else if (string.Equals(currentHash, file.Sha256Before, StringComparison.Ordinal))
                {
                    status = UndoFileStatus.AlreadyRestored;
                }
                else if (!string.Equals(currentHash, file.Sha256After, StringComparison.Ordinal))
                {
                    status = UndoFileStatus.Conflict;
                }
                else if (!file.Existed)
                {
                    Guard(() => File.Delete(path), path, "delete");
                    status = UndoFileStatus.Deleted;
                }
                else
                {
                    var permissions = FileReplacementPermissions.FromUnixAndWindows(file.UnixMode, file.WindowsReadOnly);
                    status = RestoreOriginalBytes(trustedRoot, file, path, item.Original!, item.LinkText, permissions);
                }

                results.Add(new UndoFileResult(file.Path, status));
            }

            var result = new UndoResult(manifest.BackupId, results);
            if (!result.HasConflicts && markUndone)
                WriteUndoneAt(manifestPath, manifest);

            return result;
        }
        finally
        {
            if (ownsLock)
                installationLock.Dispose();
        }
    }

    public void MarkBackupUndone(string installationId, string backupId)
    {
        RequireValidIds(installationId, "-");
        if (!BackupIdFormat.IsValid(backupId))
            throw new ArgumentException("Invalid backup id.", nameof(backupId));

        using var installationLock = AcquireInstallationLock(installationId);
        // FindBackup hides already-undone backups. A missing or already-marked manifest is success so Undo can clear the journal.
        var manifestPath = Path.Combine(_backupRoot, installationId, backupId, ManifestName);
        var bytes = ReadIfExists(manifestPath);
        if (bytes is null)
            return;

        var manifest = BackupManifest.FromJson(bytes, manifestPath);
        if (manifest.UndoneAt is not null)
            return;

        WriteUndoneAt(manifestPath, manifest);
    }

    private void WriteUndoneAt(string manifestPath, BackupManifest manifest)
    {
        var done = manifest with { UndoneAt = _clock() };
        Guard(() => _writer.WriteAllBytes(manifestPath, done.ToJson()), manifestPath, "update manifest");
    }

    private (string ManifestPath, BackupManifest Manifest, byte[] Bytes)? FindBackup(string installationId, string backupId)
    {
        var manifestPath = Path.Combine(_backupRoot, installationId, backupId, ManifestName);
        var bytes = ReadIfExists(manifestPath);
        if (bytes is null)
            return null;
        var manifest = BackupManifest.FromJson(bytes, manifestPath);
        if (manifest.UndoneAt is not null)
            return null;
        return (manifestPath, manifest, bytes);
    }

    // ---- backup ----

    private string WriteBackup(ClientFilesPlan plan, string clientRoot, string backupId, string backupDir, List<Target> targets)
    {
        var files = new List<ManifestFile>();
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            string? backupFile = null;
            string? symlinkSha256 = null;
            if (t.Original is not null)
            {
                backupFile = $"files/{i}";
                var dest = Path.Combine(backupDir, "files", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Guard(() => _writer.WriteAllBytes(dest, t.Original), dest, "write backup file");
                if (t.Symlink?.IsSymlink == true && !string.IsNullOrEmpty(t.Symlink.Value.Target))
                {
                    var sidecar = dest + ".symlink";
                    var bytes = Encoding.UTF8.GetBytes(t.Symlink.Value.Target);
                    Guard(() => _writer.WriteAllBytes(sidecar, bytes), sidecar, "write symlink sidecar");
                    symlinkSha256 = ClientFilesIo.Sha256Hex(bytes);
                }
            }

            files.Add(new ManifestFile(
                t.Edit.RelativePath,
                t.Original is not null,
                backupFile,
                t.Edit.Sha256Before,
                t.Edit.Sha256After,
                t.Permissions?.UnixMode,
                t.Permissions?.WindowsReadOnly,
                symlinkSha256));
        }

        var manifest = new BackupManifest(
            BackupManifest.CurrentVersion,
            backupId,
            plan.InstallationId,
            plan.ProfileId,
            clientRoot,
            plan.Version.ToString(),
            _clock(),
            null,
            files);
        var manifestBytes = manifest.ToJson();
        var manifestPath = Path.Combine(backupDir, ManifestName);
        Guard(() => _writer.WriteAllBytes(manifestPath, manifestBytes), manifestPath, "write manifest");
        return ClientFilesIo.Sha256Hex(manifestBytes);
    }

    private (string ManifestPath, BackupManifest Manifest, byte[] Bytes)? FindPending(string installationId)
    {
        var dir = Path.Combine(_backupRoot, installationId);
        if (!Directory.Exists(dir))
            return null;

        var ids = Guard(() => Directory.EnumerateDirectories(dir).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).Reverse().ToList(), dir, "list backups");
        foreach (var id in ids)
        {
            var manifestPath = Path.Combine(dir, id, ManifestName);
            var bytes = ReadIfExists(manifestPath);
            if (bytes is null)
                continue; // a backup directory that never got its manifest never touched the client
            var manifest = BackupManifest.FromJson(bytes, manifestPath);
            if (manifest.UndoneAt is null)
                return (manifestPath, manifest, bytes);
        }

        return null;
    }

    private static byte[] ReadBackupFile(string backupDir, ManifestFile file)
    {
        var path = Path.Combine(backupDir, file.BackupFile ?? throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, backupDir, $"The manifest has no backup file for '{file.Path}'."));
        var bytes = ReadRegularBackupFile(path);
        if (ClientFilesIo.Sha256Hex(bytes) != file.Sha256Before)
            throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path, $"The backup copy '{path}' was modified; it cannot be restored safely.");
        return bytes;
    }

    /// <summary>
    /// Opens <paramref name="path"/> once, without following a final symlink, and reads it only when that same
    /// stream is a regular file no larger than <see cref="MaxBackupFileBytes"/>. There is no separate path check
    /// before the open. A symlink, FIFO, directory, or other device is <see cref="ClientFilesErrorCode.VerifyFailed"/>.
    /// A missing file is <see cref="ClientFilesErrorCode.PathNotFound"/>.
    /// </summary>
    private static byte[] ReadRegularBackupFile(string path)
    {
        FileStream stream;
        try
        {
            stream = SymlinkSnapshot.OpenNoFollowForRead(path);
        }
        catch (ClientFilesException ex) when (ex.Code == ClientFilesErrorCode.VerifyFailed
            && ex.Message.Contains("not a regular file", StringComparison.Ordinal))
        {
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                "The backup copy is not a regular file.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ClientFilesIo.Map(ex, path, "read backup file");
        }

        using (stream)
        {
            try
            {
                if (!SymlinkSnapshot.IsSeekableRegularFile(stream, out var length))
                    throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                        "The backup copy is not a regular file.");
                if (length > MaxBackupFileBytes)
                    throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                        $"The backup copy is larger than {MaxBackupFileBytes} bytes.");

                var size = checked((int)length);
                var bytes = new byte[size];
                var offset = 0;
                while (offset < size)
                {
                    var n = stream.Read(bytes, offset, size - offset);
                    if (n == 0)
                        throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                            "The backup copy changed while it was read.");
                    offset += n;
                }

                return bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw ClientFilesIo.Map(ex, path, "read backup file");
            }
        }
    }

    /// <summary>
    /// A sidecar is used only when the entry declares <c>symlink_sha256</c> and the bytes match.
    /// A sidecar that is present without that field, or whose hash differs, is <see cref="ClientFilesErrorCode.HashMismatch"/>.
    /// </summary>
    private static string? ReadBoundSidecar(string backupDir, ManifestFile file)
    {
        if (file.BackupFile is null)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, backupDir, $"The manifest has no backup file for '{file.Path}'.");

        var path = Path.Combine(backupDir, file.BackupFile + ".symlink");
        if (!SymlinkSnapshot.NodeExists(path))
        {
            if (file.SymlinkSha256 is not null)
                throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path,
                    $"The symlink sidecar '{path}' is missing; it cannot be restored safely.");
            return null;
        }

        var (bytes, text) = SymlinkSnapshot.ReadSidecar(path);
        if (file.SymlinkSha256 is null)
            throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path,
                $"The symlink sidecar '{path}' is not bound by symlink_sha256; it cannot be restored safely.");
        if (!string.Equals(ClientFilesIo.Sha256Hex(bytes), file.SymlinkSha256, StringComparison.Ordinal))
            throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path,
                $"The symlink sidecar '{path}' was modified; it cannot be restored safely.");
        return text;
    }

    private UndoFileStatus RestoreOriginalBytes(
        string clientRoot,
        ManifestFile file,
        string path,
        byte[] original,
        string? linkText,
        FileReplacementPermissions? permissions)
    {
        if (linkText is not null && SymlinkSnapshot.ChainStaysInsideClient(clientRoot, path, linkText))
        {
            try
            {
                Guard(() => new SymlinkSnapshot(true, linkText).Restore(path), path, "restore symlink");
                byte[]? through = null;
                var comparable = false;
                Guard(() =>
                {
                    through = ReadCurrentForUndo(path, out var notComparable);
                    comparable = !notComparable;
                }, path, "read back");
                if (comparable && through is not null
                    && string.Equals(ClientFilesIo.Sha256Hex(through), file.Sha256Before, StringComparison.Ordinal))
                    return UndoFileStatus.Restored;
            }
            catch (ClientFilesException)
            {
                // Creating the link or reading through it failed. The original bytes are written below.
            }

            // Only the link is removed. Its target, which may live elsewhere in the client, is left as it is.
            SymlinkSnapshot.DeleteLinkOnly(path);
        }

        Guard(() => _writer.WriteAllBytes(path, original, permissions), path, "restore");
        var bytes = Guard(() => File.ReadAllBytes(path), path, "read back");
        if (ClientFilesIo.Sha256Hex(bytes) != file.Sha256Before)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"'{path}' does not hold its original bytes after restoring.");
        return UndoFileStatus.Restored;
    }

    // ---- helpers ----

    /// <summary>Puts every attempted file back as it was; returns false when some file could not be reverted.</summary>
    private bool Revert(List<Target> attempted)
    {
        var ok = true;
        foreach (var t in attempted)
        {
            try
            {
                if (t.Original is null)
                {
                    if (File.Exists(t.Path))
                        File.Delete(t.Path);
                }
                else if (!ClientFilesIo.Sha256Hex(ReadIfExists(t.Path) ?? []).Equals(t.Edit.Sha256Before, StringComparison.Ordinal))
                {
                    var linkRestored = false;
                    if (t.Symlink?.IsSymlink == true)
                    {
                        try
                        {
                            t.Symlink.Value.Restore(t.Path);
                            linkRestored = true;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Creating the link failed after the path was unlinked: drop any leftover link so the fallback
                            // below writes the original bytes. A missing file would make a later Undo report a conflict.
                            SymlinkSnapshot.DeleteLinkOnly(t.Path);
                        }
                    }

                    if (!linkRestored)
                        _writer.WriteAllBytes(t.Path, t.Original, t.Permissions?.ToReplacementPermissions());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ClientFilesException)
            {
                ok = false; // keep reverting the other files; the caller sees the original failure
            }
        }

        return ok;
    }

    /// <summary>
    /// Serializes Apply and Undo per installation, across threads and processes, with an exclusive lock file
    /// (<c>&lt;backupRoot&gt;/&lt;installationId&gt;/.lock</c>) held for the whole check-and-write sequence.
    /// </summary>
    private FileStream AcquireLock(string installationId)
    {
        var dir = Path.Combine(_backupRoot, installationId);
        var path = Path.Combine(dir, ".lock");
        Guard(() => Directory.CreateDirectory(dir), dir, "create backup folder");
        var deadline = DateTime.UtcNow + _lockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ClientFilesException(ClientFilesErrorCode.PermissionDenied, path,
                    $"Another operation is already running for installation '{installationId}' (or '{path}' is not accessible): {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Keeps the newest <c>keepBackups</c> backups of the installation and deletes the older ones (best effort: a failure only leaves them for next time).
    /// The newest backup that is still pending undo is never deleted. Folders without a manifest are not ours and stay.
    /// </summary>
    private void Prune(string installationId)
    {
        try
        {
            var dir = Path.Combine(_backupRoot, installationId);
            var ids = Directory.EnumerateDirectories(dir).Select(Path.GetFileName).OfType<string>()
                .Where(id => File.Exists(Path.Combine(dir, id, ManifestName)))
                .Order(StringComparer.Ordinal).Reverse().ToList();
            var pending = FindPending(installationId)?.Manifest.BackupId;
            foreach (var id in ids.Skip(_keepBackups).Where(id => id != pending))
                TryDeleteDirectory(Path.Combine(dir, id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ClientFilesException)
        {
            // Retention is housekeeping; never fail an apply that already succeeded.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// <c>yyyyMMddTHHmmssfffZ-NNNNNN</c>: the UTC time plus a sequence number. Called under the installation lock, it is always greater
    /// than the newest existing id (same millisecond or a clock that went backwards bump the sequence), so ids sort in apply order.
    /// </summary>
    private string NewBackupId(string installationId)
    {
        var timestamp = $"{_clock().UtcDateTime:yyyyMMdd'T'HHmmssfff}Z";
        var sequence = 0;
        var dir = Path.Combine(_backupRoot, installationId);
        var latest = Guard(() => Directory.EnumerateDirectories(dir).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).LastOrDefault(), dir, "list backups");
        var dash = latest?.IndexOf('-') ?? -1;
        if (latest is not null && dash > 0)
        {
            var latestTimestamp = latest[..dash];
            if (string.CompareOrdinal(timestamp, latestTimestamp) <= 0)
            {
                timestamp = latestTimestamp;
                sequence = int.TryParse(latest[(dash + 1)..], out var n) ? n + 1 : 0;
            }
        }

        return $"{timestamp}-{sequence.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static void RequireValidIds(string installationId, string profileId)
    {
        if (!ClientFilesRequest.IsValidId(installationId))
            throw new ArgumentException("InstallationId must be 1-64 characters of letters, digits, '.', '_' or '-'.", nameof(installationId));
        if (profileId != "-" && !ClientFilesRequest.IsValidId(profileId))
            throw new ArgumentException("ProfileId must be 1-64 characters of letters, digits, '.', '_' or '-'.", nameof(profileId));
    }

    private static byte[]? ReadIfExists(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ClientFilesIo.Map(ex, path, "read");
        }
    }

    /// <summary>
    /// Bytes of the managed file for the undo hash check, or null when the path is missing.
    /// <paramref name="notComparable"/> is set when the file is not a regular file or is larger than
    /// <see cref="MaxBackupFileBytes"/>. The caller records <see cref="UndoFileStatus.Conflict"/> and does not
    /// treat those bytes as a match for <c>sha256_after</c>. The read is bounded by the stream length.
    /// </summary>
    private static byte[]? ReadCurrentForUndo(string path, out bool notComparable)
    {
        notComparable = false;
        FileStream stream;
        try
        {
            stream = SymlinkSnapshot.OpenFollowNonBlockingForRead(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        using (stream)
        {
            if (!SymlinkSnapshot.IsSeekableRegularFile(stream, out var length) || length > MaxBackupFileBytes)
            {
                notComparable = true;
                return null;
            }

            var size = checked((int)length);
            var bytes = new byte[size];
            var offset = 0;
            while (offset < size)
            {
                var n = stream.Read(bytes, offset, size - offset);
                if (n == 0)
                {
                    notComparable = true;
                    return null;
                }

                offset += n;
            }

            return bytes;
        }
    }

    private static void Guard(Action action, string path, string what) => Guard<object?>(() => { action(); return null; }, path, what);

    private static T Guard<T>(Func<T> action, string path, string what)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ClientFilesIo.Map(ex, path, what);
        }
    }

    private sealed record Target(
        ClientFileEdit Edit,
        string Path,
        byte[]? Original,
        FilePermissionSnapshot? Permissions,
        SymlinkSnapshot? Symlink);

    private sealed record PreparedUndo(ManifestFile File, string Path, byte[]? Original, string? LinkText);
}
