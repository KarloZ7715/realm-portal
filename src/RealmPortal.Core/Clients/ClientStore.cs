using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Clients;

/// <summary>The user's registered client folders, kept in <c>clients.json</c> inside the config directory.</summary>
/// <remarks>
/// File format: <c>{ "version": 1, "clients": [ { "id", "path", "name", "origin" } ] }</c>; older version 1 entries without name/origin still load.
/// </remarks>
public sealed class ClientStore(string configDirectory, IAtomicFileWriter writer, ConfigDirectoryLock? directoryLock = null)
{
    public const string FileName = "clients.json";
    private const int FormatVersion = 1;
    private readonly ConfigDirectoryLock _directoryLock = directoryLock ?? new ConfigDirectoryLock(configDirectory);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
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
    public StoredClient Add(string folderPath, string? name = null, ClientOrigin origin = ClientOrigin.Manual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        name = string.IsNullOrEmpty(name) ? null : name;
        ValidateName(name);
        var full = ClientPathSecurity.CanonicalizeTrustedFolder(folderPath);
        var id = ClientIds.FromFolderPath(full);
        using (_directoryLock.Acquire())
        {
            var clients = Load().ToList();
            var existing = clients.FirstOrDefault(c => c.Id == id);
            if (existing is not null)
                return existing;

            clients.Add(new StoredClient(id, full, name, origin));
            Save(clients);
            return clients[^1];
        }
    }

    public bool Remove(string id)
    {
        using (_directoryLock.Acquire())
            return RemoveCore(id);
    }

    public StoredClient? Update(string id, string path, string? name, ClientOrigin origin = ClientOrigin.Manual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        name = string.IsNullOrEmpty(name) ? null : name;
        ValidateName(name);
        var full = ClientPathSecurity.CanonicalizeTrustedFolder(path);
        var updated = new StoredClient(ClientIds.FromFolderPath(full), full, name, origin);
        using (_directoryLock.Acquire())
            return ReplaceCore(id, updated);
    }

    internal StoredClient? ReplaceCore(string id, StoredClient updated)
    {
        updated = updated with { Name = string.IsNullOrEmpty(updated.Name) ? null : updated.Name };
        ValidateName(updated.Name);
        var clients = Load().ToList();
        var index = clients.FindIndex(c => c.Id == id);
        if (index < 0)
            return null;
        if (clients.Any(c => c.Id == updated.Id && c.Id != id))
            throw new StoreException($"A client with id '{updated.Id}' already exists.");
        clients[index] = updated;
        Save(clients);
        return updated;
    }

    internal bool RemoveCore(string id)
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
            writer.WriteAllBytes(FilePath, new System.Text.UTF8Encoding(false).GetBytes(json), PrivateStorage.PrivateFilePermissions);
            PortalConfigFile.AfterWrite(FilePath);
        });
    }

    private static StoredClient ToClient(ClientRecord r)
    {
        if (string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Path))
            throw new StoreException("A stored client is missing id or path.");

        // Existence and symlink checks happen when the folder is used, not when the record is loaded.
        var full = ClientPathSecurity.NormalizeStoredFolder(r.Path);
        var expectedId = ClientIds.FromFolderPath(full);
        if (!string.Equals(expectedId, r.Id, StringComparison.Ordinal))
            throw new StoreException($"Stored client id '{r.Id}' does not match folder '{full}'.");

        try
        {
            ValidateName(r.Name);
            if (!Enum.IsDefined(r.Origin))
                throw new ArgumentException("Unknown client origin.", nameof(r.Origin));
        }
        catch (ArgumentException ex)
        {
            throw new StoreException($"A stored client is invalid: {ex.Message}", ex);
        }

        return new StoredClient(expectedId, full, r.Name, r.Origin);
    }

    private static ClientRecord ToRecord(StoredClient c) => new() { Id = c.Id, Path = c.Path, Name = c.Name, Origin = c.Origin };

    private static void ValidateName(string? name)
    {
        if (name is null)
            return;
        if (name.Length > 64 || name.EnumerateRunes().Any(r => Rune.IsControl(r)
                || Rune.GetUnicodeCategory(r) is System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.LineSeparator
                    or System.Globalization.UnicodeCategory.ParagraphSeparator
                || r.Value is '"' or '\\'))
            throw new ArgumentException("Client name must be at most 64 characters and contain no control, format, quote or backslash characters.", "name");
    }

    private sealed class StoreFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("clients")] public List<ClientRecord>? Clients { get; set; }
    }

    private sealed class ClientRecord
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("origin")] public ClientOrigin Origin { get; set; } = ClientOrigin.Manual;
    }
}

/// <summary>A client folder registered in <see cref="ClientStore"/>.</summary>
public sealed record StoredClient(string Id, string Path, string? Name = null, ClientOrigin Origin = ClientOrigin.Manual);
