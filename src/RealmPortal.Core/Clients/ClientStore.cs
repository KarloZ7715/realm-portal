using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Clients;

/// <summary>The user's registered client folders, kept in <c>clients.json</c> inside the config directory.</summary>
/// <remarks>
/// File format: <c>{ "version": 1, "clients": [ { "id", "path" } ] }</c>.
/// </remarks>
public sealed class ClientStore(string configDirectory, IAtomicFileWriter writer)
{
    public const string FileName = "clients.json";
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string FilePath { get; } = Path.Combine(configDirectory, FileName);

    public IReadOnlyList<StoredClient> List() => Load();

    public StoredClient? FindById(string id) => Load().FirstOrDefault(c => c.Id == id);

    public StoredClient? FindByPath(string folderPath)
    {
        var id = ClientIds.FromFolderPath(folderPath);
        return FindById(id);
    }

    /// <summary>Adds a client or returns the existing entry for the same folder.</summary>
    public StoredClient Add(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var full = ClientPathSecurity.CanonicalizeTrustedFolder(folderPath);
        var id = ClientIds.FromFolderPath(full);
        var clients = Load().ToList();
        var existing = clients.FirstOrDefault(c => c.Id == id);
        if (existing is not null)
            return existing;

        clients.Add(new StoredClient(id, full));
        Save(clients);
        return clients[^1];
    }

    public bool Remove(string id)
    {
        var clients = Load().ToList();
        if (clients.RemoveAll(c => c.Id == id) == 0)
            return false;

        Save(clients);
        return true;
    }

    private List<StoredClient> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            return PortalConfigFile.WithConfigLock(() =>
            {
                var file = JsonSerializer.Deserialize<StoreFile>(PortalConfigFile.ReadBounded(FilePath), JsonOptions)
                           ?? throw new StoreException($"'{FilePath}' is empty.");
                if (file.Version != FormatVersion)
                    throw new StoreException($"'{FilePath}' has unsupported format version {file.Version}.");

                return (file.Clients ?? []).Select(ToClient).ToList();
            });
        }
        catch (JsonException ex)
        {
            throw new StoreException($"'{FilePath}' is not valid: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new StoreException($"'{FilePath}' cannot be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StoreException($"'{FilePath}' cannot be read: {ex.Message}", ex);
        }
    }

    private void Save(List<StoredClient> clients)
    {
        PortalConfigFile.WithConfigLock(() =>
        {
            var file = new StoreFile { Version = FormatVersion, Clients = clients.Select(ToRecord).ToList() };
            var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
            writer.WriteAllBytes(FilePath, new System.Text.UTF8Encoding(false).GetBytes(json));
            PortalConfigFile.AfterWrite(FilePath);
        });
    }

    private static StoredClient ToClient(ClientRecord r)
    {
        if (string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Path))
            throw new StoreException("A stored client is missing id or path.");

        var full = ClientPathSecurity.CanonicalizeTrustedFolder(r.Path);
        var expectedId = ClientIds.FromFolderPath(full);
        if (!string.Equals(expectedId, r.Id, StringComparison.Ordinal))
            throw new StoreException($"Stored client id '{r.Id}' does not match folder '{full}'.");

        return new StoredClient(expectedId, full);
    }

    private static ClientRecord ToRecord(StoredClient c) => new() { Id = c.Id, Path = c.Path };

    private sealed class StoreFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("clients")] public List<ClientRecord>? Clients { get; set; }
    }

    private sealed class ClientRecord
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
    }
}

/// <summary>A client folder registered in <see cref="ClientStore"/>.</summary>
public sealed record StoredClient(string Id, string Path);
