using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

/// <summary>
/// Remembers the last successful switch for coordinated undo (<c>last-switch.json</c> in the config directory).
/// </summary>
internal sealed class LastSwitchJournal(string configDirectory, IAtomicFileWriter writer)
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
        PortalConfigFile.WithConfigLock(() =>
        {
            var file = new JournalFile
            {
                Version = FormatVersion,
                ClientId = entry.ClientId,
                BackupId = entry.BackupId,
                FilesUndone = entry.FilesUndone,
            };
            var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
            writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json));
            PortalConfigFile.AfterWrite(_path);
        });
    }

    public void MarkFilesUndone()
    {
        var entry = Load() ?? throw new StoreException("No last-switch journal entry to update.");
        Save(entry with { FilesUndone = true });
    }

    public void MarkFilesNotUndone()
    {
        var entry = Load() ?? throw new StoreException("No last-switch journal entry to update.");
        Save(entry with { FilesUndone = false });
    }

    public void Clear()
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

    internal static void ValidateEntry(LastSwitchEntry entry)
    {
        if (entry.ClientId is not null && !ClientFilesRequest.IsValidId(entry.ClientId))
            throw new StoreException("last-switch client_id is not valid.");
        if (entry.BackupId is not null && !BackupIdFormat.IsValid(entry.BackupId))
            throw new StoreException("last-switch backup_id is not valid.");
        if (entry.BackupId is not null && entry.ClientId is null)
            throw new StoreException("last-switch backup_id requires client_id.");
    }

    private static LastSwitchEntry Parse(JournalFile file)
    {
        if (file.Version != FormatVersion)
            throw new StoreException($"Unsupported last-switch format version {file.Version}.");
        var clientId = string.IsNullOrWhiteSpace(file.ClientId) ? null : file.ClientId;

        var entry = new LastSwitchEntry(clientId, file.BackupId, file.FilesUndone);
        ValidateEntry(entry);
        return entry;
    }

    private sealed class JournalFile
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("client_id")] public string? ClientId { get; init; }
        [JsonPropertyName("backup_id")] public string? BackupId { get; init; }
        [JsonPropertyName("files_undone")] public bool FilesUndone { get; init; }
    }
}

internal sealed record LastSwitchEntry(string? ClientId, string? BackupId, bool FilesUndone = false);
