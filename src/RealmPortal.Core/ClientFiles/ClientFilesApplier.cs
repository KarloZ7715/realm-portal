using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

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
    /// A managed config file larger than this is refused before capture or apply.
    /// </summary>
    public const int MaxManagedConfigBytes = 1_048_576;
    internal const int MaxBackupFileBytes = 16 * 1024 * 1024;
    private const int MaxManifestBytes = 256 * 1024;

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
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keepBackups, 50);
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
            if (edit.NewBytes.Length > MaxManagedConfigBytes)
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, edit.RelativePath,
                    $"Managed config file is larger than {MaxManagedConfigBytes} bytes.");
            var path = ClientPaths.ResolveManagedPath(clientRoot, edit.RelativePath);
            var symlink = SymlinkSnapshot.Capture(path);
            var original = SymlinkSnapshot.NodeExists(path) ? ReadExistingForBackup(clientRoot, path, symlink) : null;
            if (original?.Length > MaxManagedConfigBytes)
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path,
                    $"Managed config file is larger than {MaxManagedConfigBytes} bytes.");
            var currentHash = original is null ? null : ClientFilesIo.Sha256Hex(original);
            if (!string.Equals(currentHash, edit.Sha256Before, StringComparison.Ordinal))
            {
                throw new ClientFilesException(ClientFilesErrorCode.HashMismatch, path,
                    $"'{path}' changed since the plan was made; plan again.");
            }

            var permissions = FilePermissionSnapshot.Capture(path);
            if (permissions is not null && FilePermissionSnapshot.IsReadOnly(path))
                warnings.Add($"'{edit.RelativePath}' was read-only; its permissions were preserved.");

            targets.Add(new Target(edit, path, original, permissions, symlink));
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

    public IReadOnlyList<BackupEntry> ListBackups(string? installationId = null)
    {
        if (installationId is not null)
            RequireValidIds(installationId, "-");
        IEnumerable<string> clients = installationId is null
            ? SafeDirectories(_backupRoot).Select(Path.GetFileName).OfType<string>().Where(ClientFilesRequest.IsValidId)
            : new[] { installationId };
        var entries = new List<BackupEntry>();
        foreach (var client in clients)
        {
            foreach (var dir in SafeDirectories(Path.Combine(_backupRoot, client)))
            {
                var id = Path.GetFileName(dir);
                if (!BackupIdFormat.IsValid(id)) continue;
                var path = Path.Combine(dir, ManifestName);
                try
                {
                    var bytes = ReadBoundedNoFollow(path, MaxManifestBytes);
                    var manifest = BackupManifest.FromJson(bytes, path);
                    if (manifest.InstallationId != client || manifest.BackupId != id) continue;
                    entries.Add(ToBackupEntry(manifest, false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ClientFilesException or JsonException)
                {
                    entries.Add(new BackupEntry(id, client, string.Empty, string.Empty, BackupCreatedAtFromId(id), 0, [], Error: ex.Message));
                }
            }
        }
        return entries.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.BackupId, StringComparer.Ordinal).ToArray();
    }

    private static DateTimeOffset BackupCreatedAtFromId(string backupId) =>
        backupId.Length >= 19 && DateTimeOffset.TryParseExact(backupId[..19], "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : DateTimeOffset.MinValue;

    public BackupEntry CreateBackup(string installationId, string clientRoot, string? label = null)
    {
        RequireValidIds(installationId, "-");
        if (label?.Length > 256) throw new ArgumentException("Backup label must be 256 characters or fewer.", nameof(label));
        var root = ClientPaths.CanonicalRoot(clientRoot);
        using var installationLock = AcquireInstallationLock(installationId);
        if (_probe.IsGameRunning(root))
            throw new ClientFilesException(ClientFilesErrorCode.GameRunning, root, "The game is running; close it before backing up its files.");

        var targets = CaptureManagedTargets(root);
        if (targets.Count == 0)
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, root, "No managed client configuration files were found to back up.");
        var latest = ListBackups(installationId).FirstOrDefault();
        if (latest is not null && IsSameSnapshot(latest, targets))
            return latest with { WasDeduplicated = true };

        var backupId = NewBackupId(installationId);
        var backupDir = Path.Combine(_backupRoot, installationId, backupId);
        var manifest = WriteManualBackup(installationId, root, backupId, backupDir, targets, label);
        PruneLocked(installationId, _keepBackups);
        return ToBackupEntry(manifest, false);
    }

    public RestoreBackupOutcome RestoreBackup(string installationId, string clientRoot, string backupId, bool force = false)
    {
        RequireValidIds(installationId, "-");
        if (!BackupIdFormat.IsValid(backupId))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, backupId ?? string.Empty, "The backup id is invalid.");
        var root = ClientPaths.CanonicalRoot(clientRoot);
        using var installationLock = AcquireInstallationLock(installationId);
        if (_probe.IsGameRunning(root))
            throw new ClientFilesException(ClientFilesErrorCode.GameRunning, root, "The game is running; close it before restoring its files.");
        var found = ReadNamedBackup(installationId, backupId);
        if (found is null)
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, backupId, $"Backup '{backupId}' was not found.");
        var (manifestPath, manifest) = found.Value;
        ClientPaths.RequireMatchingRoots(root, manifest.ClientRoot, manifestPath);
        var backupDir = Path.GetDirectoryName(manifestPath)!;
        BackupManifest.EnsureBackupFilesReferenced(backupDir, manifest.Files);

        var prepared = new List<PreparedUndo>(manifest.Files.Count);
        foreach (var file in manifest.Files)
        {
            var path = ClientPaths.ResolveManagedPath(root, file.Path);
            var original = file.Existed ? ReadBackupFile(backupDir, file) : null;
            var linkText = file.Existed ? ReadBoundSidecar(backupDir, file) : null;
            var current = ReadCurrentForUndo(path, out var notComparable);
            var hash = notComparable || current is null ? null : ClientFilesIo.Sha256Hex(current);
            var allowed = !notComparable && (hash == file.Sha256Before || hash == file.Sha256After);
            if (!force && !allowed)
                throw new ClientFilesException(ClientFilesErrorCode.UndoConflict, path,
                    $"'{file.Path}' changed since this backup; no files were restored.");
            prepared.Add(new PreparedUndo(file, path, original, linkText));
        }

        string? safetyBackupId = null;
        if (force)
        {
            var safety = CaptureManagedTargets(root);
            if (safety.Count > 0)
            {
                var latest = ListBackups(installationId).FirstOrDefault();
                if (latest is null || !IsSameSnapshot(latest, safety))
                {
                    var safetyId = NewBackupId(installationId);
                    WriteManualBackup(installationId, root, safetyId, Path.Combine(_backupRoot, installationId, safetyId), safety, "pre-restore");
                    safetyBackupId = safetyId;
                }
            }
        }

        var results = new List<UndoFileResult>(prepared.Count);
        foreach (var item in prepared)
        {
            if (item.File.Existed)
            {
                var permissions = FileReplacementPermissions.FromUnixAndWindows(item.File.UnixMode, item.File.WindowsReadOnly);
                var status = RestoreOriginalBytes(root, item.File, item.Path, item.Original!, item.LinkText, permissions);
                results.Add(new UndoFileResult(item.File.Path, status));
            }
            else
            {
                Guard(() => File.Delete(item.Path), item.Path, "delete");
                results.Add(new UndoFileResult(item.File.Path, UndoFileStatus.Deleted));
            }
        }
        WriteUndoneAt(manifestPath, manifest);
        PruneLocked(installationId, _keepBackups);
        return new RestoreBackupOutcome(backupId, safetyBackupId, results);
    }

    public void DeleteBackup(string installationId, string backupId)
    {
        RequireValidIds(installationId, "-");
        if (!BackupIdFormat.IsValid(backupId))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, backupId ?? string.Empty, "The backup id is invalid.");
        using var installationLock = AcquireInstallationLock(installationId);
        var dir = Path.Combine(_backupRoot, installationId, backupId);
        var manifestPath = Path.Combine(dir, ManifestName);
        if (!Directory.Exists(dir) || new DirectoryInfo(dir).LinkTarget is not null || !SymlinkSnapshot.NodeExists(manifestPath))
            throw new ClientFilesException(ClientFilesErrorCode.PathNotFound, dir, $"Backup '{backupId}' was not found.");
        try
        {
            var manifestBytes = ReadBoundedNoFollow(manifestPath, MaxManifestBytes);
            var manifest = BackupManifest.FromJson(manifestBytes, manifestPath);
            if (manifest.InstallationId != installationId || manifest.BackupId != backupId)
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, manifestPath, "The manifest identity does not match its folder.");
            if (_probe.IsGameRunning(manifest.ClientRoot))
                throw new ClientFilesException(ClientFilesErrorCode.GameRunning, manifest.ClientRoot, "The game is running; close it before deleting a backup.");
        }
        catch (ClientFilesException ex) when (ex.Code == ClientFilesErrorCode.VerifyFailed)
        {
            // A damaged snapshot is still removable; the service checks the registered client process before this call.
        }
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw ClientFilesIo.Map(ex, dir, "delete backup"); }
    }

    public void PruneBackups(string? installationId = null, int? keepBackups = null)
    {
        if (installationId is not null) RequireValidIds(installationId, "-");
        var keep = keepBackups ?? _keepBackups;
        if (keep is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(keepBackups), "KeepBackups must be from 1 through 50.");
        IEnumerable<string> clients = installationId is null
            ? SafeDirectories(_backupRoot).Select(Path.GetFileName).OfType<string>().Where(ClientFilesRequest.IsValidId).ToArray()
            : new[] { installationId };
        foreach (var client in clients)
        {
            using var installationLock = AcquireInstallationLock(client);
            PruneLocked(client, keep);
        }
    }

    private void PruneLocked(string installationId, int keep)
    {
        var entries = ListBackups(installationId);
        var pending = entries.FirstOrDefault(x => x.UndoneAt is null && !x.IsManual)?.BackupId;
        foreach (var old in entries.Skip(keep).Where(x => x.BackupId != pending))
            TryDeleteDirectory(Path.Combine(_backupRoot, installationId, old.BackupId));
    }

    private void WriteUndoneAt(string manifestPath, BackupManifest manifest)
    {
        var done = manifest with { UndoneAt = _clock() };
        Guard(() => _writer.WriteAllBytes(manifestPath, done.ToJson(), PrivateStorage.PrivateFilePermissions), manifestPath, "update manifest");
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
        Guard(() => PrivateStorage.CreatePrivateDirectory(_backupRoot), _backupRoot, "create backup root");
        var installationBackupDir = Path.GetDirectoryName(backupDir)!;
        Guard(() => PrivateStorage.CreatePrivateDirectory(installationBackupDir), installationBackupDir, "create installation backup folder");
        Guard(() => PrivateStorage.CreatePrivateDirectory(backupDir), backupDir, "create backup folder");
        var filesDirectory = Path.Combine(backupDir, "files");
        Guard(() => PrivateStorage.CreatePrivateDirectory(filesDirectory), filesDirectory, "create backup files folder");
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
                Guard(() => _writer.WriteAllBytes(dest, t.Original, PrivateStorage.PrivateFilePermissions), dest, "write backup file");
                if (t.Symlink?.IsSymlink == true && !string.IsNullOrEmpty(t.Symlink.Value.Target))
                {
                    var sidecar = dest + ".symlink";
                    var bytes = Encoding.UTF8.GetBytes(t.Symlink.Value.Target);
                    Guard(() => _writer.WriteAllBytes(sidecar, bytes, PrivateStorage.PrivateFilePermissions), sidecar, "write symlink sidecar");
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
        Guard(() => _writer.WriteAllBytes(manifestPath, manifestBytes, PrivateStorage.PrivateFilePermissions), manifestPath, "write manifest");
        return ClientFilesIo.Sha256Hex(manifestBytes);
    }

    private List<ManualTarget> CaptureManagedTargets(string root)
    {
        var candidates = new List<string[]> { new[] { "realmlist.wtf" }, new[] { "WTF", "Config.wtf" } };
        var data = CaseInsensitivePath.FindDirectory(root, "Data");
        if (data is not null)
        {
            foreach (var locale in CaseInsensitivePath.ChildDirectories(Path.Combine(root, data)))
                if (System.Text.RegularExpressions.Regex.IsMatch(locale, "^[a-z]{2}[A-Z]{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    candidates.Add([data, locale, "realmlist.wtf"]);
        }

        var targets = new List<ManualTarget>();
        foreach (var candidate in candidates)
        {
            var (segments, exists) = CaseInsensitivePath.Resolve(root, candidate, rejectAmbiguousMatches: true);
            if (!exists) continue;
            var relative = string.Join('/', segments);
            var path = ClientPaths.ResolveManagedPath(root, relative);
            var link = SymlinkSnapshot.Capture(path);
            var bytes = ReadManagedBounded(root, path, link);
            if (bytes.Length > MaxManagedConfigBytes)
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"Managed config file is larger than {MaxManagedConfigBytes} bytes.");
            targets.Add(new ManualTarget(relative, path, bytes, link?.IsSymlink == true ? null : FilePermissionSnapshot.Capture(path), link));
        }
        return targets;
    }

    private BackupManifest WriteManualBackup(string installationId, string clientRoot, string backupId, string backupDir, List<ManualTarget> targets, string? label)
    {
        Guard(() => PrivateStorage.CreatePrivateDirectory(Path.GetDirectoryName(backupDir)!), Path.GetDirectoryName(backupDir)!, "create installation backup folder");
        Guard(() => PrivateStorage.CreatePrivateDirectory(backupDir), backupDir, "create backup folder");
        var filesDir = Path.Combine(backupDir, "files");
        Guard(() => PrivateStorage.CreatePrivateDirectory(filesDir), filesDir, "create backup files folder");
        var entries = new List<ManifestFile>(targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t.Bytes.Length > MaxManagedConfigBytes)
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, t.Path, $"Managed config file is larger than {MaxManagedConfigBytes} bytes.");
            var backupFile = $"files/{i}";
            Guard(() => _writer.WriteAllBytes(Path.Combine(filesDir, i.ToString(System.Globalization.CultureInfo.InvariantCulture)), t.Bytes, PrivateStorage.PrivateFilePermissions), filesDir, "write backup file");
            string? linkHash = null;
            if (t.Symlink?.IsSymlink == true && !string.IsNullOrEmpty(t.Symlink.Value.Target))
            {
                var linkBytes = Encoding.UTF8.GetBytes(t.Symlink.Value.Target);
                Guard(() => _writer.WriteAllBytes(Path.Combine(filesDir, i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".symlink"), linkBytes, PrivateStorage.PrivateFilePermissions), filesDir, "write symlink sidecar");
                linkHash = ClientFilesIo.Sha256Hex(linkBytes);
            }
            var hash = ClientFilesIo.Sha256Hex(t.Bytes);
            entries.Add(new ManifestFile(t.RelativePath, true, backupFile, hash, hash, t.Permissions?.UnixMode, t.Permissions?.WindowsReadOnly, linkHash));
        }
        var manifest = new BackupManifest(BackupManifest.CurrentVersion, backupId, installationId, "manual", clientRoot,
            "unknown", _clock(), null, entries, true, label);
        var manifestBytes = manifest.ToJson();
        if (manifestBytes.Length > MaxManifestBytes)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, backupDir, "The generated backup manifest exceeds the size limit.");
        Guard(() => _writer.WriteAllBytes(Path.Combine(backupDir, ManifestName), manifestBytes, PrivateStorage.PrivateFilePermissions), backupDir, "write manifest");
        return manifest;
    }

    private bool IsSameSnapshot(BackupEntry latest, List<ManualTarget> targets)
    {
        var found = ReadNamedBackup(latest.ClientId, latest.BackupId);
        if (found is null || found.Value.Manifest.Files.Count != targets.Count) return false;
        var prior = found.Value.Manifest.Files.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var current = targets.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < prior.Length; i++)
        {
            var p = prior[i]; var c = current[i];
            var payload = p.BackupFile is null ? null : ReadBackupFile(Path.GetDirectoryName(found.Value.Path)!, p);
            if (!string.Equals(p.Path, c.RelativePath, StringComparison.OrdinalIgnoreCase)
                || payload is null || ClientFilesIo.Sha256Hex(payload) != ClientFilesIo.Sha256Hex(c.Bytes)
                || p.UnixMode != c.Permissions?.UnixMode || p.WindowsReadOnly != c.Permissions?.WindowsReadOnly)
                return false;
            var link = ReadBoundSidecar(Path.GetDirectoryName(found.Value.Path)!, p);
            if (!string.Equals(link, c.Symlink?.Target, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private (string Path, BackupManifest Manifest)? ReadNamedBackup(string installationId, string backupId)
    {
        var path = Path.Combine(_backupRoot, installationId, backupId, ManifestName);
        if (!SymlinkSnapshot.NodeExists(path)) return null;
        var bytes = ReadBoundedNoFollow(path, MaxManifestBytes);
        var manifest = BackupManifest.FromJson(bytes, path);
        if (manifest.InstallationId != installationId || manifest.BackupId != backupId)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, "The manifest identity does not match its folder.");
        return (path, manifest);
    }

    private BackupEntry ToBackupEntry(BackupManifest manifest, bool deduplicated)
    {
        var dir = Path.Combine(_backupRoot, manifest.InstallationId, manifest.BackupId);
        long total = 0;
        foreach (var file in manifest.Files.Where(f => f.BackupFile is not null))
            total += ReadRegularBackupFile(Path.Combine(dir, file.BackupFile!)).LongLength;
        return new BackupEntry(manifest.BackupId, manifest.InstallationId, manifest.ClientRoot, manifest.ProfileId,
            manifest.CreatedAt, total, manifest.Files.Select(f => f.Path).ToArray(), manifest.UndoneAt,
            manifest.IsManual, manifest.Label, deduplicated);
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateDirectories(path).ToArray() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static byte[] ReadManagedBounded(string root, string path, SymlinkSnapshot? link)
    {
        if (link?.IsSymlink == true)
        {
            if (string.IsNullOrEmpty(link.Value.Target) || !SymlinkSnapshot.ChainStaysInsideClient(root, path, link.Value.Target))
                throw new ClientFilesException(ClientFilesErrorCode.PathEscapesClient, path, "Managed config symlink does not resolve to a regular file inside the client.");
        }
        else
        {
            var bytesWithoutFollowing = ReadBoundedNoFollow(path, MaxManagedConfigBytes);
            return bytesWithoutFollowing;
        }
        var bytes = ReadCurrentForUndo(path, out var invalid);
        if (invalid || bytes is null)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, "Managed config file is not a readable regular file.");
        if (bytes.Length > MaxManagedConfigBytes)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"Managed config file is larger than {MaxManagedConfigBytes} bytes.");
        return bytes;
    }

    private static byte[] ReadExistingForBackup(string root, string path, SymlinkSnapshot? link)
    {
        if (link?.IsSymlink != true)
            return ReadBoundedNoFollow(path, MaxManagedConfigBytes);
        // Existing apply/undo semantics preserve even links that point outside the client, but their payload read
        // remains non-blocking and capped. Undo recreates a link only when its chain is proved to stay inside root.
        var bytes = ReadCurrentForUndo(path, out var invalid);
        if (invalid || bytes is null)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, "Managed config symlink does not resolve to a readable regular file.");
        if (bytes.Length > MaxManagedConfigBytes)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"Managed config file is larger than {MaxManagedConfigBytes} bytes.");
        return bytes;
    }

    private static byte[] ReadBoundedNoFollow(string path, int maxBytes)
    {
        using var stream = SymlinkSnapshot.OpenNoFollowForRead(path);
        if (!SymlinkSnapshot.IsSeekableRegularFile(stream, out var length) || length > maxBytes)
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"File is not a regular file within the {maxBytes}-byte limit.");
        var bytes = new byte[checked((int)length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, "File changed while it was read.");
            offset += read;
        }
        return bytes;
    }

    private sealed record ManualTarget(string RelativePath, string Path, byte[] Bytes, FilePermissionSnapshot? Permissions, SymlinkSnapshot? Symlink);

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
            if (manifest.UndoneAt is null && !manifest.IsManual)
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
        Guard(() => PrivateStorage.CreatePrivateDirectory(_backupRoot), _backupRoot, "create backup root");
        Guard(() => PrivateStorage.CreatePrivateDirectory(dir), dir, "create backup folder");
        var deadline = DateTime.UtcNow + _lockTimeout;
        while (true)
        {
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, PrivateStorage.PrivateFileMode);
                return stream;
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
            return File.Exists(path) ? PrivateStorage.ReadBounded(path, PrivateStorage.MaxJsonBytes) : null;
        }
        catch (StoreException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, ex.Message, ex);
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
