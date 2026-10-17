using System.Text.Json;
using System.Text.Json.Serialization;

namespace RealmPortal.Core.ClientFiles;

/// <summary>One file of a backup (see docs/client-files-backup.md).</summary>
internal sealed record ManifestFile(
    string Path,
    bool Existed,
    string? BackupFile,
    string? Sha256Before,
    string Sha256After);

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

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

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
            return manifest;
        }
        catch (JsonException ex)
        {
            throw new ClientFilesException(ClientFilesErrorCode.VerifyFailed, path, $"The backup manifest '{path}' is unreadable: {ex.Message}", ex);
        }
    }
}
