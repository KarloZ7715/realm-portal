using System.Text.Json;
using System.Text.Json.Serialization;

namespace RealmPortal.Core.ClientFiles;

/// <summary>One file of a backup (see docs/client-files-backup.md).</summary>
internal sealed record ManifestFile(
    string Path,
    bool Existed,
    string? BackupFile,
    string? Sha256Before,
    string Sha256After,
    bool IsSymlink = false,
    string? SymlinkTarget = null);

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

    private static readonly System.Text.RegularExpressions.Regex BackupFileName = new("^files/[0-9]{1,6}\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
        foreach (var f in Files)
        {
            if (f is null || string.IsNullOrWhiteSpace(f.Path) || string.IsNullOrWhiteSpace(f.Sha256After))
                throw new JsonException("A file entry is missing path or sha256_after.");
            if (f.Existed && (string.IsNullOrWhiteSpace(f.Sha256Before) || f.BackupFile is null || !BackupFileName.IsMatch(f.BackupFile)))
                throw new JsonException($"The entry for '{f.Path}' lacks its sha256_before or a valid backup_file.");
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
