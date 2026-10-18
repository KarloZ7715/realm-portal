using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

/// <summary>
/// Remembers the last successful switch for coordinated undo (<c>last-switch.json</c> in the config directory).
/// <c>manifest_sha256</c> is optional. Journals written before the field existed omit it and still load.
/// </summary>
internal sealed class LastSwitchJournal(string configDirectory, IAtomicFileWriter writer, ConfigDirectoryLock? directoryLock = null)
{
    public const string FileName = "last-switch.json";
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path = Path.Combine(configDirectory, FileName);
    private readonly ConfigDirectoryLock _directoryLock = directoryLock ?? new ConfigDirectoryLock(configDirectory);

    public LastSwitchEntry? Load()
    {
        if (!File.Exists(_path))
            return null;

        return PortalConfigFile.WithConfigLock(() =>
        {
            try
            {
                var file = JsonSerializer.Deserialize<JournalFile>(PortalConfigFile.ReadBounded(_path), JsonOptions)
                           ?? throw new StoreException($"'{_path}' is empty.");
                return Parse(file);
            }
            catch (JsonException ex)
            {
                throw new StoreException($"'{_path}' is not valid: {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                throw new StoreException($"'{_path}' cannot be read: {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new StoreException($"'{_path}' cannot be read: {ex.Message}", ex);
            }
        });
    }

    public void Save(LastSwitchEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateEntry(entry);
        using (_directoryLock.Acquire())
            Write(entry);
    }

    public void MarkFilesUndone()
    {
        // Load and save under one lock, so two processes cannot drop each other's flag change.
        using (_directoryLock.Acquire())
        {
            var entry = Load() ?? throw new StoreException("No last-switch journal entry to update.");
            Write(entry with { FilesUndone = true });
        }
    }

    public void MarkFilesNotUndone()
    {
        using (_directoryLock.Acquire())
        {
            var entry = Load() ?? throw new StoreException("No last-switch journal entry to update.");
            Write(entry with { FilesUndone = false });
        }
    }

    public void Clear()
    {
        using (_directoryLock.Acquire())
        {
            PortalConfigFile.WithConfigLock(() =>
            {
                try
                {
                    if (File.Exists(_path))
                        File.Delete(_path);
                }
                catch (IOException ex)
                {
                    throw new StoreException($"Cannot delete '{_path}': {ex.Message}", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new StoreException($"Cannot delete '{_path}': {ex.Message}", ex);
                }
            });
        }
    }

    /// <summary>Writes the entry; callers already hold <see cref="ConfigDirectoryLock"/>.</summary>
    private void Write(LastSwitchEntry entry)
    {
        PortalConfigFile.WithConfigLock(() =>
        {
            var file = new JournalFile
            {
                Version = FormatVersion,
                ClientId = entry.ClientId,
                BackupId = entry.BackupId,
                FilesUndone = entry.FilesUndone,
                ManifestSha256 = entry.ManifestSha256,
            };
            var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
            writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json));
            PortalConfigFile.AfterWrite(_path);
        });
    }

    internal static void ValidateEntry(LastSwitchEntry entry)
    {
        if (entry.ClientId is not null && !ClientFilesRequest.IsValidId(entry.ClientId))
            throw new StoreException("last-switch client_id is not valid.");
        if (entry.BackupId is not null && !BackupIdFormat.IsValid(entry.BackupId))
            throw new StoreException("last-switch backup_id is not valid.");
        if (entry.BackupId is not null && entry.ClientId is null)
            throw new StoreException("last-switch backup_id requires client_id.");
        if (entry.ManifestSha256 is not null && !IsSha256Hex(entry.ManifestSha256))
            throw new StoreException("last-switch manifest_sha256 is not valid.");
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
            return false;
        foreach (var c in value)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }

    private static LastSwitchEntry Parse(JournalFile file)
    {
        if (file.Version != FormatVersion)
            throw new StoreException($"Unsupported last-switch format version {file.Version}.");
        var clientId = string.IsNullOrWhiteSpace(file.ClientId) ? null : file.ClientId;

        var entry = new LastSwitchEntry(clientId, file.BackupId, file.FilesUndone, file.ManifestSha256);
        ValidateEntry(entry);
        return entry;
    }

    private sealed class JournalFile
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("client_id")] public string? ClientId { get; init; }
        [JsonPropertyName("backup_id")] public string? BackupId { get; init; }
        [JsonPropertyName("files_undone")] public bool FilesUndone { get; init; }
        [JsonPropertyName("manifest_sha256")] public string? ManifestSha256 { get; init; }
    }
}

/// <param name="ManifestSha256">
/// SHA-256 of the backup <c>manifest.json</c> bytes, or <c>null</c> when the switch wrote no backup
/// or the journal was written before this field existed.
/// </param>
internal sealed record LastSwitchEntry(string? ClientId, string? BackupId, bool FilesUndone = false, string? ManifestSha256 = null);
