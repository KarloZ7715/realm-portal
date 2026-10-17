using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        try
        {
            var file = JsonSerializer.Deserialize<JournalFile>(File.ReadAllBytes(_path), JsonOptions)
                       ?? throw new StoreException($"'{_path}' is empty.");
            if (file.Version != FormatVersion)
                throw new StoreException($"'{_path}' has unsupported format version {file.Version}.");
            if (string.IsNullOrWhiteSpace(file.ClientId))
                throw new StoreException($"'{_path}' is missing client_id.");
            return new LastSwitchEntry(file.ClientId, file.BackupId);
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
    }

    public void Save(LastSwitchEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var file = new JournalFile { Version = FormatVersion, ClientId = entry.ClientId, BackupId = entry.BackupId };
        var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
        writer.WriteAllBytes(_path, new System.Text.UTF8Encoding(false).GetBytes(json));
    }

    public void Clear()
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

    private sealed class JournalFile
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("client_id")] public string? ClientId { get; init; }
        [JsonPropertyName("backup_id")] public string? BackupId { get; init; }
    }
}

internal sealed record LastSwitchEntry(string ClientId, string? BackupId);
