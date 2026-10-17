using System.Security.Cryptography;
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

    public ApplyResult Apply(ClientFilesPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RequireValidIds(plan.InstallationId, plan.ProfileId);
        if (plan.IsEmpty)
            return new ApplyResult(null, []);

        if (_probe.IsGameRunning(plan.ClientRoot))
            throw new ClientFilesException(ClientFilesErrorCode.GameRunning, plan.ClientRoot, "The game is running; close it before changing its files.");

        using var installationLock = AcquireLock(plan.InstallationId);

        // Read and verify the current state of every file before touching anything.
        var targets = new List<Target>();
        foreach (var edit in plan.Edits)
        {
            var path = ResolveInside(plan.ClientRoot, edit.RelativePath);
            var original = ReadIfExists(path);
            var currentHash = original is null ? null : ClientFilesIo.Sha256Hex(original);
            if (!string.Equals(currentHash, edit.Sha256Before, StringComparison.Ordinal))
            {
                throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path,
                    $"'{path}' changed since the plan was made; plan again.");
            }

            targets.Add(new Target(edit, path, original));
        }

        var backupId = NewBackupId();
        var backupDir = Path.Combine(_backupRoot, plan.InstallationId, backupId);
        try
        {
            WriteBackup(plan, backupId, backupDir, targets);
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
                Guard(() => _writer.WriteAllBytes(target.Path, target.Edit.NewBytes), target.Path, "write");
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
        return new ApplyResult(backupId, plan.Edits);
    }

    public UndoResult Undo(string installationId)
    {
        RequireValidIds(installationId, "-");
        using var installationLock = AcquireLock(installationId);
        var (manifestPath, manifest) = FindPending(installationId)
            ?? throw new ClientFilesException(ClientFilesErrorCode.NothingToUndo, Path.Combine(_backupRoot, installationId),
                $"There is no applied change to undo for '{installationId}'.");

        if (_probe.IsGameRunning(manifest.ClientRoot))
            throw new ClientFilesException(ClientFilesErrorCode.GameRunning, manifest.ClientRoot, "The game is running; close it before restoring its files.");

        var backupDir = Path.GetDirectoryName(manifestPath)!;
        var results = new List<UndoFileResult>();
        foreach (var file in manifest.Files)
        {
            var path = ResolveInside(manifest.ClientRoot, file.Path);
            var current = ReadIfExists(path);
            var currentHash = current is null ? null : ClientFilesIo.Sha256Hex(current);

            UndoFileStatus status;
            if (string.Equals(currentHash, file.Sha256Before, StringComparison.Ordinal))
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
                var original = ReadBackupFile(backupDir, file);
                Guard(() => _writer.WriteAllBytes(path, original), path, "restore");
                var restored = Guard(() => File.ReadAllBytes(path), path, "read back");
                if (ClientFilesIo.Sha256Hex(restored) != file.Sha256Before)
                    throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"'{path}' does not hold its original bytes after restoring.");
                status = UndoFileStatus.Restored;
            }

            results.Add(new UndoFileResult(file.Path, status));
        }

        var result = new UndoResult(manifest.BackupId, results);
        if (!result.HasConflicts)
        {
            // Only a fully undone backup is retired; one with conflicts stays pending so nothing older is undone on top of it.
            var done = manifest with { UndoneAt = _clock() };
            Guard(() => _writer.WriteAllBytes(manifestPath, done.ToJson()), manifestPath, "update manifest");
        }

        return result;
    }

    // ---- backup ----

    private void WriteBackup(ClientFilesPlan plan, string backupId, string backupDir, List<Target> targets)
    {
        var files = new List<ManifestFile>();
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            string? backupFile = null;
            if (t.Original is not null)
            {
                backupFile = $"files/{i}";
                var dest = Path.Combine(backupDir, "files", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Guard(() => _writer.WriteAllBytes(dest, t.Original), dest, "write backup file");
            }

            files.Add(new ManifestFile(
                t.Edit.RelativePath,
                t.Original is not null,
                backupFile,
                t.Edit.Sha256Before,
                t.Edit.Sha256After));
        }

        var manifest = new BackupManifest(
            BackupManifest.CurrentVersion,
            backupId,
            plan.InstallationId,
            plan.ProfileId,
            plan.ClientRoot,
            plan.Version.ToString(),
            _clock(),
            null,
            files);
        var manifestPath = Path.Combine(backupDir, ManifestName);
        Guard(() => _writer.WriteAllBytes(manifestPath, manifest.ToJson()), manifestPath, "write manifest");
    }

    private (string ManifestPath, BackupManifest Manifest)? FindPending(string installationId)
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
                return (manifestPath, manifest);
        }

        return null;
    }

    private static byte[] ReadBackupFile(string backupDir, ManifestFile file)
    {
        var path = Path.Combine(backupDir, file.BackupFile ?? throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, backupDir, $"The manifest has no backup file for '{file.Path}'."));
        var bytes = Guard(() => File.ReadAllBytes(path), path, "read backup file");
        if (ClientFilesIo.Sha256Hex(bytes) != file.Sha256Before)
            throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path, $"The backup copy '{path}' was modified; it cannot be restored safely.");
        return bytes;
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
                    _writer.WriteAllBytes(t.Path, t.Original);
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

    private string NewBackupId() =>
        $"{_clock().UtcDateTime:yyyyMMdd'T'HHmmssfff}Z-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}";

    private static void RequireValidIds(string installationId, string profileId)
    {
        if (!ClientFilesRequest.IsValidId(installationId))
            throw new ArgumentException("InstallationId must be 1-64 characters of letters, digits, '.', '_' or '-'.", nameof(installationId));
        if (profileId != "-" && !ClientFilesRequest.IsValidId(profileId))
            throw new ArgumentException("ProfileId must be 1-64 characters of letters, digits, '.', '_' or '-'.", nameof(profileId));
    }

    /// <summary>Joins a '/'-separated relative path below the client root, refusing anything that could leave it.</summary>
    private static string ResolveInside(string root, string relative)
    {
        var segments = relative.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || s.Contains('\\') || s.Contains(':')))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, relative, $"'{relative}' is not a relative path inside the client folder.");
        return Path.Combine([root, .. segments]);
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

    private sealed record Target(ClientFileEdit Edit, string Path, byte[]? Original);
}
