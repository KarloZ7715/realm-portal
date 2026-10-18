using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RealmPortal.Core.ClientFiles;

/// <summary>One file of a backup (see docs/client-files-backup.md).</summary>
/// <param name="SymlinkSha256">SHA-256 of <c>files/&lt;n&gt;.symlink</c> when this file was a symlink; otherwise null.</param>
internal sealed record ManifestFile(
    string Path,
    bool Existed,
    string? BackupFile,
    string? Sha256Before,
    string Sha256After,
    int? UnixMode = null,
    bool? WindowsReadOnly = null,
    string? SymlinkSha256 = null);

/// <summary>The <c>manifest.json</c> that describes one backup.</summary>
internal sealed record BackupManifest(
    int Version,
    string BackupId,
    string InstallationId,
    string ProfileId,
    string ClientRoot,
    string ClientBuild,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UndoneAt,
    IReadOnlyList<ManifestFile> Files)
{
    public const int CurrentVersion = 1;

    private static readonly Regex BackupFileName = new("^files/[0-9]{1,6}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex PayloadFileName = new("^[0-9]{1,6}(\\.symlink)?\\z", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientRoot))
            throw new JsonException("Missing client_root.");
        // Apply never writes a backup for an empty plan, so an empty list is not a backup we produced.
        if (Files.Count == 0)
            throw new JsonException("The manifest has no files.");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backupFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in Files)
        {
            if (f is null || string.IsNullOrWhiteSpace(f.Path) || string.IsNullOrWhiteSpace(f.Sha256After))
                throw new JsonException("A file entry is missing path or sha256_after.");
            // A cloned entry can share a backup file and also name a traversal path. The allow-list
            // check comes first so Undo reports PathEscapesClient before backup-file uniqueness.
            ClientPaths.ValidateManagedRelativePath(f.Path);
            if (!paths.Add(f.Path))
                throw new JsonException($"Two entries share the path '{f.Path}'.");

            if (f.Existed)
            {
                if (string.IsNullOrWhiteSpace(f.Sha256Before) || f.BackupFile is null || !BackupFileName.IsMatch(f.BackupFile))
                    throw new JsonException($"The entry for '{f.Path}' lacks its sha256_before or a valid backup_file.");
                if (!backupFiles.Add(f.BackupFile))
                    throw new JsonException($"Two entries share the backup file '{f.BackupFile}'.");
                if (f.SymlinkSha256 is not null && string.IsNullOrWhiteSpace(f.SymlinkSha256))
                    throw new JsonException($"The entry for '{f.Path}' has an empty symlink_sha256.");
            }
            else if (f.BackupFile is not null || f.Sha256Before is not null || f.SymlinkSha256 is not null)
            {
                throw new JsonException($"The entry for '{f.Path}' did not exist and must not name a backup or symlink.");
            }

            if (f.UnixMode is int mode && !Core.IO.FileReplacementPermissions.IsValidUnixMode(mode))
                throw new JsonException($"The entry for '{f.Path}' has unix_mode {mode}, outside 0..0777.");
        }
    }

    /// <summary>
    /// Every <c>files/&lt;n&gt;</c> and <c>files/&lt;n&gt;.symlink</c> in the backup folder must belong to an entry.
    /// A payload left behind means an entry was removed or flipped to <c>existed: false</c>.
    /// A file the switch created has no payload, so removing that entry cannot be detected here; Undo then leaves the file in place.
    /// </summary>
    public static void EnsureBackupFilesReferenced(string backupDir, IReadOnlyList<ManifestFile> files)
    {
        var filesDir = Path.Combine(backupDir, "files");
        if (!Directory.Exists(filesDir))
            return;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (file.BackupFile is not null)
                referenced.Add(file.BackupFile);
        }

        List<string> present;
        try
        {
            present = Directory.EnumerateFiles(filesDir).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ClientFilesIo.Map(ex, filesDir, "list backup files");
        }

        foreach (var full in present)
        {
            var name = Path.GetFileName(full);
            if (name is null || !PayloadFileName.IsMatch(name))
                continue;

            var stem = name.EndsWith(".symlink", StringComparison.Ordinal) ? name[..^".symlink".Length] : name;
            if (!referenced.Contains("files/" + stem))
                throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, full,
                    $"The backup file '{full}' is not referenced by the manifest.");
        }
    }

    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    public static BackupManifest FromJson(byte[] json, string path)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<BackupManifest>(json, Options);
            if (manifest is null || manifest.Files is null || string.IsNullOrEmpty(manifest.BackupId))
                throw new JsonException("Missing required fields.");
            if (manifest.Version != CurrentVersion)
                throw new JsonException($"Unsupported manifest version {manifest.Version}.");
            manifest.Validate();
            return manifest;
        }
        catch (JsonException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"The backup manifest '{path}' is unreadable: {ex.Message}", ex);
        }
    }
}
