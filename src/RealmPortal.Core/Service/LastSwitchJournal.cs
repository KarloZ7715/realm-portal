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

    public LastSwitchEntry? Load(string? clientId = null)
    {
        if (!File.Exists(_path))
            return null;

        return PortalConfigFile.WithConfigLock(() =>
        {
            try
            {
                var file = JsonSerializer.Deserialize<JournalFile>(PortalConfigFile.ReadBounded(_path), JsonOptions)
                           ?? throw new StoreException($"'{_path}' is empty.");
                if (clientId is not null && file.ByClient?.TryGetValue(clientId, out var perClient) == true)
                    return ParseEntry(clientId, perClient);
                var legacy = Parse(file);
                return clientId is null || legacy?.ClientId == clientId ? legacy : null;
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
        {
            var existing = ReadFile();
            var byClient = existing?.ByClient is null
                ? new Dictionary<string, JournalEntry>(StringComparer.Ordinal)
                : new Dictionary<string, JournalEntry>(existing.ByClient, StringComparer.Ordinal);
            // Migrate a version-1 legacy entry into the map before its fields are replaced by this write, so a
            // pending switch for the legacy client survives saving a different client (or a state-only entry).
            if (existing is { ByClient: null } && existing.ClientId is { } legacyClientId)
            {
                byClient.TryAdd(
                    legacyClientId,
                    JournalEntry.From(new LastSwitchEntry(legacyClientId, existing.BackupId, existing.FilesUndone, existing.ManifestSha256)));
            }
            if (entry.ClientId is { } clientId)
                byClient[clientId] = JournalEntry.From(entry);
            WriteFile(new JournalFile
            {
                Version = FormatVersion,
                ClientId = entry.ClientId,
                BackupId = entry.BackupId,
                FilesUndone = entry.FilesUndone,
                ManifestSha256 = entry.ManifestSha256,
                ByClient = byClient,
            });
        }
    }

    public void MarkFilesUndone(string? clientId = null)
    {
        UpdateFilesUndone(true, clientId);
    }

    public void MarkFilesNotUndone(string? clientId = null)
    {
        UpdateFilesUndone(false, clientId);
    }

    private void UpdateFilesUndone(bool value, string? clientId)
    {
        using (_directoryLock.Acquire())
        {
            var file = ReadFile() ?? throw new StoreException("No last-switch journal entry to update.");
            var entry = clientId is null ? Parse(file) : Load(clientId);
            if (entry is null)
                throw new StoreException("No last-switch journal entry to update.");
            var updated = entry with { FilesUndone = value };
            var byClient = file.ByClient is null
                ? new Dictionary<string, JournalEntry>(StringComparer.Ordinal)
                : new Dictionary<string, JournalEntry>(file.ByClient, StringComparer.Ordinal);
            if (updated.ClientId is { } updatedClientId && byClient.ContainsKey(updatedClientId))
                byClient[updatedClientId] = JournalEntry.From(updated);
            var updateLegacy = clientId is null || file.ClientId == clientId;
            WriteFile(file with { FilesUndone = updateLegacy ? value : file.FilesUndone, ByClient = byClient });
        }
    }

    public void Clear(string? clientId = null)
    {
        using (_directoryLock.Acquire())
        {
            var file = ReadFile();
            if (file is null)
                return;

            if (clientId is null)
            {
                // Clear only the version-1 global/legacy view. Per-client entries (and their undos) survive.
                if (file.ByClient is { Count: > 0 })
                    WriteFile(file with { ClientId = null, BackupId = null, FilesUndone = false, ManifestSha256 = null });
                else
                    DeleteFile();
                return;
            }

            var byClient = file.ByClient is null
                ? new Dictionary<string, JournalEntry>(StringComparer.Ordinal)
                : new Dictionary<string, JournalEntry>(file.ByClient, StringComparer.Ordinal);

            byClient.Remove(clientId);
            if (file.ClientId == clientId)
                PromoteOrDelete(file, byClient);
            else
                WriteFile(file with { ByClient = byClient });
        }
    }

    /// <summary>Promotes the surviving per-client entry to the legacy view, or deletes the file when none remain.</summary>
    private void PromoteOrDelete(JournalFile file, Dictionary<string, JournalEntry> byClient)
    {
        if (byClient.Count == 0)
        {
            DeleteFile();
            return;
        }

        var replacement = byClient.First();
        WriteFile(file with
        {
            ClientId = replacement.Key,
            BackupId = replacement.Value.BackupId,
            FilesUndone = replacement.Value.FilesUndone,
            ManifestSha256 = replacement.Value.ManifestSha256,
            ByClient = byClient,
        });
    }

    private void DeleteFile()
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
    }

    private JournalFile? ReadFile()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<JournalFile>(PortalConfigFile.ReadBounded(_path), JsonOptions)
                   ?? throw new StoreException($"'{_path}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new StoreException($"'{_path}' is not valid: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new StoreException($"'{_path}' cannot be read: {ex.Message}", ex);
        }
    }

    private void WriteFile(JournalFile file)
    {
        PortalConfigFile.WithConfigLock(() =>
        {
            var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
            writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json), PrivateStorage.PrivateFilePermissions);
            PortalConfigFile.AfterWrite(_path);
        });
    }

    private static LastSwitchEntry ParseEntry(string clientId, JournalEntry entry)
    {
        var parsed = new LastSwitchEntry(clientId, entry.BackupId, entry.FilesUndone, entry.ManifestSha256);
        ValidateEntry(parsed);
        return parsed;
    }

    /*
     * Existing methods below retain the version-1 single-entry format as the global compatibility view;
     * the optional by_client map adds independent journals without invalidating old files.
     */

    /// <summary>Writes the entry; callers already hold <see cref="ConfigDirectoryLock"/>.</summary>
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

    private sealed record JournalFile
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("client_id")] public string? ClientId { get; init; }
        [JsonPropertyName("backup_id")] public string? BackupId { get; init; }
        [JsonPropertyName("files_undone")] public bool FilesUndone { get; init; }
        [JsonPropertyName("manifest_sha256")] public string? ManifestSha256 { get; init; }
        [JsonPropertyName("by_client")] public Dictionary<string, JournalEntry>? ByClient { get; init; }
    }

    private sealed record JournalEntry
    {
        [JsonPropertyName("backup_id")] public string? BackupId { get; init; }
        [JsonPropertyName("files_undone")] public bool FilesUndone { get; init; }
        [JsonPropertyName("manifest_sha256")] public string? ManifestSha256 { get; init; }
        public static JournalEntry From(LastSwitchEntry entry) => new() { BackupId = entry.BackupId, FilesUndone = entry.FilesUndone, ManifestSha256 = entry.ManifestSha256 };
    }
}

/// <param name="ManifestSha256">
/// SHA-256 of the backup <c>manifest.json</c> bytes, or <c>null</c> when the switch wrote no backup
/// or the journal was written before this field existed.
/// </param>
internal sealed record LastSwitchEntry(string? ClientId, string? BackupId, bool FilesUndone = false, string? ManifestSha256 = null);
