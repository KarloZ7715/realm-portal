using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Servers;

/// <summary>
/// The user's list of servers, kept in <c>servers.json</c> inside the config directory.
/// There is no bundled list: the file only ever contains servers the user added.
/// </summary>
/// <remarks>
/// File format (UTF-8 without BOM, 2-space indent, <c>\n</c> newlines, trailing newline):
/// <c>{ "version": 1, "servers": [ { "id", "name", "host", "realm", "realmlist", "required_client_build", "account", "client_id" } ] }</c>.
/// A missing file is an empty store; an unreadable or unsupported file raises <see cref="StoreException"/>
/// and is never overwritten.
/// </remarks>
public sealed class ServerStore(string configDirectory, IAtomicFileWriter writer, ConfigDirectoryLock? directoryLock = null)
{
    public const string FileName = "servers.json";
    private const int FormatVersion = 1;
    private readonly ConfigDirectoryLock _directoryLock = directoryLock ?? new ConfigDirectoryLock(configDirectory);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string FilePath { get; } = Path.Combine(configDirectory, FileName);

    /// <summary>Lock shared with <see cref="ActiveServerManager"/> for cross-process serialization.</summary>
    public ConfigDirectoryLock DirectoryLock => _directoryLock;

    public IReadOnlyList<Server> List() => Load();

    /// <summary>Finds a server by exact id, then by case-insensitive name, then by unique id prefix.</summary>
    public Server? Find(string selector)
    {
        var servers = Load();
        var found = servers.FirstOrDefault(s => s.Id == selector)
                    ?? servers.FirstOrDefault(s => string.Equals(s.Name, selector, StringComparison.OrdinalIgnoreCase));
        if (found is not null || selector.Length == 0)
            return found;

        var byPrefix = servers.Where(s => s.Id.StartsWith(selector, StringComparison.Ordinal)).Take(2).ToList();
        return byPrefix.Count == 1 ? byPrefix[0] : null;
    }

    /// <summary>Adds a server. Ids and names (case-insensitive) must be unique.</summary>
    /// <exception cref="StoreException">The id or the name is already taken.</exception>
    /// <exception cref="InvalidRealmlistException">The server's realmlist breaks the realmlist contract; nothing is written.</exception>
    public Server Add(Server server)
    {
        ArgumentNullException.ThrowIfNull(server);
        try
        {
            ServerValidator.Validate(server);
        }
        catch (InvalidRealmlistException ex)
        {
            throw ex.ForServer(server.Name);
        }

        using (_directoryLock.Acquire())
        {
            var servers = Load().ToList();
            if (servers.Any(s => s.Id == server.Id))
                throw new StoreException($"A server with id '{server.Id}' already exists.");
            if (servers.Any(s => string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase)))
                throw new StoreException($"A server named '{server.Name}' already exists.");

            servers.Add(server);
            Save(servers);
            return server;
        }
    }

    /// <summary>Replaces an existing server while preserving its id.</summary>
    /// <exception cref="StoreException">The server is missing or another server uses the same name.</exception>
    public Server Update(Server server)
    {
        ArgumentNullException.ThrowIfNull(server);
        try
        {
            ServerValidator.Validate(server);
        }
        catch (InvalidRealmlistException ex)
        {
            throw ex.ForServer(server.Name);
        }

        using (_directoryLock.Acquire())
            return UpdateCore(server);
    }

    internal Server UpdateCore(Server server)
    {
        var servers = Load().ToList();
        var index = servers.FindIndex(s => s.Id == server.Id);
        if (index < 0)
            throw new StoreException($"No server matches '{server.Id}'.");
        if (servers.Where((_, i) => i != index)
            .Any(s => string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase)))
            throw new StoreException($"A server named '{server.Name}' already exists.");
        servers[index] = server;
        Save(servers);
        return server;
    }

    internal void RewriteClientIdReferencesCore(string oldClientId, string? newClientId)
    {
        var servers = Load().ToList();
        var changed = false;
        for (var i = 0; i < servers.Count; i++)
        {
            if (servers[i].ClientId != oldClientId)
                continue;
            servers[i] = servers[i] with { ClientId = newClientId };
            changed = true;
        }
        if (changed)
            Save(servers);
    }

    /// <summary>Removes a server by id. Returns false when no such server exists.</summary>
    public bool Remove(string id)
    {
        using (_directoryLock.Acquire())
            return RemoveCore(id);
    }

    /// <summary>Like <see cref="Remove"/> but assumes the config lock is already held.</summary>
    internal bool RemoveCore(string id)
    {
        var servers = Load().ToList();
        if (servers.RemoveAll(s => s.Id == id) == 0)
            return false;

        Save(servers);
        return true;
    }

    private List<Server> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(PrivateStorage.ReadBounded(FilePath, PrivateStorage.MaxJsonBytes), JsonOptions)
                       ?? throw new StoreException($"'{FilePath}' is empty.");
            if (file.Version != FormatVersion)
                throw new StoreException($"'{FilePath}' has unsupported format version {file.Version}.");

            var servers = (file.Servers ?? []).Select(ToServer).ToList();
            if (servers.Select(s => s.Id).Distinct().Count() != servers.Count)
                throw new StoreException($"'{FilePath}' contains duplicate server ids.");
            return servers;
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

    private void Save(List<Server> servers)
    {
        var file = new StoreFile { Version = FormatVersion, Servers = servers.Select(ToRecord).ToList() };
        var json = JsonSerializer.Serialize(file, JsonOptions) + "\n";
        writer.WriteAllBytes(FilePath, new System.Text.UTF8Encoding(false).GetBytes(json), PrivateStorage.PrivateFilePermissions);
    }

    private static Server ToServer(ServerRecord r)
    {
        if (string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Host)
            || r.Realmlist is null)
            throw new StoreException("A stored server is missing id, name, host or realmlist.");

        var server = new Server(r.Id, r.Name, r.Host, r.Realm ?? "", r.Realmlist, r.RequiredClientBuild, r.Account, r.ClientId);
        try
        {
            ServerValidator.Validate(server);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidRealmlistException)
        {
            throw new StoreException($"A stored server is invalid: {ex.Message}", ex);
        }

        return server;
    }

    private static ServerRecord ToRecord(Server s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Host = s.Host,
        Realm = s.Realm,
        Realmlist = s.Realmlist,
        RequiredClientBuild = s.RequiredClientBuild,
        Account = s.Account,
        ClientId = s.ClientId,
    };

    private sealed class StoreFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("servers")] public List<ServerRecord>? Servers { get; set; }
    }

    private sealed class ServerRecord
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("host")] public string? Host { get; set; }
        [JsonPropertyName("realm")] public string? Realm { get; set; }
        [JsonPropertyName("realmlist")] public string? Realmlist { get; set; }
        [JsonPropertyName("required_client_build")] public int? RequiredClientBuild { get; set; }
        [JsonPropertyName("account")] public string? Account { get; set; }
        [JsonPropertyName("client_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClientId { get; set; }
    }
}
