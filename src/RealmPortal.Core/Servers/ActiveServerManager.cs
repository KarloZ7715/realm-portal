using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.Core.IO;

namespace RealmPortal.Core.Servers;

/// <summary>Result of <see cref="ActiveServerManager.Status"/>.</summary>
/// <param name="Active">The active server, or <c>null</c> when none is active.</param>
/// <param name="ProjectionInSync">True when <c>activo.json</c> holds exactly what the active server projects to (or, with no active server, does not exist).</param>
/// <param name="CanUndo">True when <see cref="ActiveServerManager.Undo"/> has something to restore.</param>
public sealed record ActiveStatus(Server? Active, bool ProjectionInSync, bool CanUndo);

/// <summary>
/// Remembers the active server between sessions (<c>state.json</c> in the config directory), keeps one level of
/// undo and projects the active server to <c>activo.json</c> in the projection directory.
/// </summary>
/// <remarks>
/// <c>state.json</c> (same byte conventions as <c>servers.json</c>):
/// <c>{ "version": 1, "active_id": string|null, "undo": null | { "server_id": string|null } }</c>.
/// <c>undo.server_id</c> is the server that was active before the last switch (<c>null</c> = none was active).
/// Order of writes: <c>state.json</c> first, then <c>activo.json</c>, so a failed projection leaves state ahead of the projection.
/// </remarks>
public sealed class ActiveServerManager(
    ServerStore store,
    string configDirectory,
    string projectionDirectory,
    IAtomicFileWriter writer)
{
    public const string StateFileName = "state.json";
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _statePath = Path.Combine(configDirectory, StateFileName);
    private readonly string _projectionPath = ActiveProjection.FilePath(projectionDirectory);

    /// <summary>The active server, or <c>null</c> when none is active (or the stored one no longer exists).</summary>
    public Server? GetActive(string? clientId = null) => Resolve(LoadState(), clientId);

    public IReadOnlyDictionary<string, Server> GetActiveByClient()
    {
        var state = LoadState();
        var servers = store.List().ToDictionary(s => s.Id, StringComparer.Ordinal);
        return state.ActiveByClient
            .Where(pair => servers.ContainsKey(pair.Value))
            .ToDictionary(pair => pair.Key, pair => servers[pair.Value], StringComparer.Ordinal);
    }

    public ActiveStatus Status()
    {
        var state = LoadState();
        var active = Resolve(state);
        return new ActiveStatus(active, IsProjectionInSync(active), state.Undo is not null);
    }

    /// <summary>The <c>server_id</c> recorded for undo, or <c>null</c> when undo would deactivate; unset when there is nothing to undo.</summary>
    public string? PendingUndoServerId => LoadState().Undo?.ServerId;

    public string? GetPendingUndoServerId(string? clientId) =>
        clientId is null ? PendingUndoServerId : LoadState().UndoByClient.GetValueOrDefault(clientId)?.ServerId;

    public bool HasPendingUndo(string? clientId = null)
    {
        var state = LoadState();
        return clientId is null ? state.Undo is not null : state.UndoByClient.ContainsKey(clientId);
    }

    public IReadOnlyList<string> ClientIdsWithUndoTo(string serverId) =>
        LoadState().UndoByClient.Where(pair => pair.Value.ServerId == serverId).Select(pair => pair.Key).ToList();

    /// <summary>The server that <see cref="Undo"/> would restore, or <c>null</c> when undo would deactivate; <c>null</c> when there is nothing to undo.</summary>
    public Server? GetUndoTarget(string? clientId = null)
    {
        var state = LoadState();
        var undo = clientId is not null ? state.UndoByClient.GetValueOrDefault(clientId) : state.Undo;
        if (undo is null)
            return null;
        return undo.ServerId is { } id
            ? store.List().FirstOrDefault(s => s.Id == id)
            : null;
    }

    /// <summary>Client ids whose per-client active state points at the given server.</summary>
    public IReadOnlyList<string> ClientIdsActiveOn(string serverId) =>
        LoadState().ActiveByClient.Where(pair => pair.Value == serverId).Select(pair => pair.Key).ToList();

    /// <summary>Migrates per-client state when a registered folder receives a new stable id.</summary>
    public void MigrateClientState(string oldClientId, string newClientId)
    {
        using (store.DirectoryLock.Acquire())
        {
            var state = LoadState();
            var active = new Dictionary<string, string>(state.ActiveByClient, StringComparer.Ordinal);
            if (active.Remove(oldClientId, out var activeServerId))
                active[newClientId] = activeServerId;
            var undo = new Dictionary<string, UndoRecord>(state.UndoByClient, StringComparer.Ordinal);
            var removedUndo = undo.Remove(oldClientId); // A backup is bound to the old installation id and client root.
            if (activeServerId is not null || removedUndo)
                SaveState(state with { ActiveByClient = active, UndoByClient = undo });
        }
    }

    /// <summary>Removes per-client active state and optionally its global undo record.</summary>
    public void RemoveClientState(string clientId, bool clearGlobalUndo)
    {
        using (store.DirectoryLock.Acquire())
        {
            var state = LoadState();
            var active = new Dictionary<string, string>(state.ActiveByClient, StringComparer.Ordinal);
            var undo = new Dictionary<string, UndoRecord>(state.UndoByClient, StringComparer.Ordinal);
            var changed = active.Remove(clientId) | undo.Remove(clientId);
            if (clearGlobalUndo && state.Undo is not null)
                changed = true;
            if (changed)
                SaveState(state with { ActiveByClient = active, UndoByClient = undo, Undo = clearGlobalUndo ? null : state.Undo });
        }
    }

    /// <summary>Sets a client's initial active server without changing an existing client-specific selection.</summary>
    public void SetActiveForClient(Server server, string clientId)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        using (store.DirectoryLock.Acquire())
        {
            var stored = store.List().FirstOrDefault(s => s.Id == server.Id)
                         ?? throw new StoreException($"Server '{server.Id}' is not in the store.");
            var state = LoadState();
            if (state.ActiveByClient.ContainsKey(clientId))
                return;

            var activeByClient = new Dictionary<string, string>(state.ActiveByClient, StringComparer.Ordinal)
            {
                [clientId] = stored.Id,
            };
            var shouldProject = state.ActiveId is null;
            SaveState(state with { ActiveId = shouldProject ? stored.Id : state.ActiveId, ActiveByClient = activeByClient });
            if (!shouldProject)
                return;
            try
            {
                writer.WriteAllBytes(_projectionPath, ActiveProjection.Serialize(stored));
            }
            catch
            {
                SaveState(state);
                throw;
            }
        }
    }

    /// <summary>Updates a server and refreshes the global projection when that server is active.</summary>
    public Server Update(Server server)
    {
        ArgumentNullException.ThrowIfNull(server);
        using (store.DirectoryLock.Acquire())
        {
            var old = store.List().FirstOrDefault(s => s.Id == server.Id)
                      ?? throw new StoreException($"No server matches '{server.Id}'.");
            var state = LoadState();
            store.UpdateCore(server);
            if (state.ActiveId != server.Id)
                return server;
            try
            {
                writer.WriteAllBytes(_projectionPath, ActiveProjection.Serialize(server));
                return server;
            }
            catch (Exception writeError)
            {
                try
                {
                    store.UpdateCore(old);
                    writer.WriteAllBytes(_projectionPath, ActiveProjection.Serialize(old));
                }
                catch (Exception rollbackError)
                {
                    throw new StoreException("Updating the active server failed and rollback also failed.", new AggregateException(writeError, rollbackError));
                }
                throw;
            }
        }
    }

    /// <summary>Clears the one-level undo record without changing the active server or projection.</summary>
    /// <remarks>The read, the check and the write happen under the config-directory lock, so a concurrent
    /// <see cref="Switch"/>, <see cref="Undo"/> or <see cref="Remove"/> in another process cannot be overwritten
    /// with a stale copy of <c>state.json</c>.</remarks>
    public void DiscardPendingUndo(string? clientId = null)
    {
        using (store.DirectoryLock.Acquire())
        {
            var state = LoadState();
            if (clientId is null)
            {
                if (state.Undo is not null)
                    SaveState(state with { Undo = null });
                return;
            }

            var undoByClient = new Dictionary<string, UndoRecord>(state.UndoByClient, StringComparer.Ordinal);
            if (undoByClient.Remove(clientId))
                SaveState(state with { UndoByClient = undoByClient });
        }
    }

    /// <summary>
    /// Makes <paramref name="server"/> the active one and writes <c>activo.json</c>. Switching to the server that is
    /// already active rewrites the projection but keeps the undo information unchanged.
    /// </summary>
    /// <exception cref="StoreException">The server is not in the store.</exception>
    /// <exception cref="InvalidRealmlistException">The stored realmlist breaks the realmlist contract; nothing is written.</exception>
    public void Switch(Server server, string? clientId = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        using (store.DirectoryLock.Acquire())
        {
            var stored = store.List().FirstOrDefault(s => s.Id == server.Id)
                         ?? throw new StoreException($"Server '{server.Name}' is not in the store.");

            var state = LoadState();
            var previous = Resolve(state, clientId);
            // The global undo record tracks the global selection, which can differ from the switching client's own
            // previous server (a client with no entry yet resolves to null while the global one still points somewhere).
            var previousGlobal = Resolve(state);

            try
            {
                RealmlistText.Validate(stored.Realmlist);
            }
            catch (InvalidRealmlistException ex)
            {
                throw ex.ForServer(stored.Name);
            }

            var activeByClient = new Dictionary<string, string>(state.ActiveByClient, StringComparer.Ordinal);
            var undoByClient = new Dictionary<string, UndoRecord>(state.UndoByClient, StringComparer.Ordinal);
            if (clientId is not null)
            {
                activeByClient[clientId] = stored.Id;
                if (previous?.Id != stored.Id)
                    undoByClient[clientId] = new UndoRecord { ServerId = previous?.Id };
            }
            var next = state with
            {
                Version = FormatVersion,
                ActiveId = stored.Id,
                Undo = previousGlobal?.Id == stored.Id ? state.Undo : new UndoRecord { ServerId = previousGlobal?.Id },
                ActiveByClient = activeByClient,
                UndoByClient = undoByClient,
            };
            SaveState(next);
            try
            {
                writer.WriteAllBytes(_projectionPath, ActiveProjection.Serialize(stored));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SaveState(state);
                throw;
            }
        }
    }

    /// <summary>
    /// Restores the server that was active before the last switch (or deactivates, deleting <c>activo.json</c>, when
    /// none was). One level only: a second undo without a switch in between fails.
    /// </summary>
    /// <returns>The restored server, or <c>null</c> when the result is "no active server".</returns>
    /// <param name="clientId">The client whose undo record is consumed, or <c>null</c> for the global record.</param>
    /// <param name="globalJournalClientId">
    /// The client recorded as owning the most recent switch (the global journal's <c>client_id</c>). A client-scoped
    /// undo updates the global <c>active_id</c>, the global undo record and the <c>activo.json</c> projection only when
    /// it matches this client; otherwise those stay with the client that owns the global selection. Also identifies the
    /// per-client entry a global undo reverts, when known.
    /// </param>
    /// <remarks>
    /// The client-scoped target and the global target are resolved separately: the client-scoped target is applied
    /// only to <c>active_by_client</c>, and the global target only to <c>active_id</c> and <c>activo.json</c>, and only
    /// when the undo owns the global selection.
    /// </remarks>
    /// <exception cref="StoreException">There is nothing to undo, or the server to restore no longer exists.</exception>
    /// <exception cref="InvalidRealmlistException">The realmlist of the server to restore is invalid; nothing is changed.</exception>
    public Server? Undo(string? clientId = null, string? globalJournalClientId = null)
    {
        using (store.DirectoryLock.Acquire())
        {
            var state = LoadState();

            // The record that decides whether there is anything to undo: the client's own record for a scoped
            // undo, the global record otherwise.
            var pending = clientId is null ? state.Undo : state.UndoByClient.GetValueOrDefault(clientId);
            if (pending is null)
                throw new StoreException("Nothing to undo.");

            // A client-scoped undo owns the global selection only when it is the client that made the most recent
            // switch; a global undo always does. The client-scoped target and the global target are resolved and
            // validated independently so a scoped undo never rewrites the global selection and a global undo never
            // borrows a client's previous server.
            var ownsGlobal = clientId is null || string.Equals(clientId, globalJournalClientId, StringComparison.Ordinal);
            var affectedClient = clientId ?? globalJournalClientId;

            var clientRecord = clientId is null
                ? (globalJournalClientId is null ? null : state.UndoByClient.GetValueOrDefault(globalJournalClientId))
                : pending;
            var clientTarget = ResolveUndoTarget(clientRecord);
            var globalTarget = ownsGlobal
                ? (state.Undo is not null ? ResolveUndoTarget(state.Undo) : clientTarget)
                : null;

            var stateBeforeUndo = state;
            var activeByClient = new Dictionary<string, string>(state.ActiveByClient, StringComparer.Ordinal);
            var undoByClient = new Dictionary<string, UndoRecord>(state.UndoByClient, StringComparer.Ordinal);
            if (affectedClient is not null)
            {
                if (clientTarget is null) activeByClient.Remove(affectedClient); else activeByClient[affectedClient] = clientTarget.Id;
                undoByClient.Remove(affectedClient);
            }

            var nextState = state with
            {
                Version = FormatVersion,
                ActiveId = ownsGlobal ? globalTarget?.Id : state.ActiveId,
                Undo = ownsGlobal ? null : state.Undo,
                ActiveByClient = activeByClient,
                UndoByClient = undoByClient,
            };
            SaveState(nextState);

            if (!ownsGlobal)
                return clientTarget;

            try
            {
                if (globalTarget is null)
                    DeleteProjection();
                else
                    writer.WriteAllBytes(_projectionPath, ActiveProjection.Serialize(globalTarget));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StoreException)
            {
                // DeleteProjection wraps I/O failures in StoreException. Roll state back so a failed
                // delete of activo.json does not commit ActiveId=null and drop the undo record.
                SaveState(stateBeforeUndo);
                throw;
            }

            return globalTarget;
        }
    }

    /// <summary>Removes a server from the store; when it was the active one, deactivates it and deletes <c>activo.json</c>.</summary>
    /// <returns>False when the server does not exist.</returns>
    public bool Remove(string id)
    {
        using (store.DirectoryLock.Acquire())
        {
            if (!store.RemoveCore(id))
                return false;

            var state = LoadState();
            var activeByClient = new Dictionary<string, string>(state.ActiveByClient, StringComparer.Ordinal);
            var undoByClient = new Dictionary<string, UndoRecord>(state.UndoByClient, StringComparer.Ordinal);
            foreach (var pair in activeByClient.Where(pair => pair.Value == id).ToList())
                activeByClient.Remove(pair.Key);
            foreach (var pair in undoByClient.Where(pair => pair.Value.ServerId == id).ToList())
                undoByClient.Remove(pair.Key);
            if (state.ActiveId == id)
            {
                SaveState(state with { ActiveId = null, Undo = null, ActiveByClient = activeByClient, UndoByClient = undoByClient });
                DeleteProjection();
            }
            else if (state.Undo?.ServerId == id || activeByClient.Count != state.ActiveByClient.Count || undoByClient.Count != state.UndoByClient.Count)
            {
                SaveState(state with { Undo = state.Undo?.ServerId == id ? null : state.Undo, ActiveByClient = activeByClient, UndoByClient = undoByClient });
            }

            return true;
        }
    }

    private Server? ResolveUndoTarget(UndoRecord? record)
    {
        if (record?.ServerId is not { } id)
            return null;
        var target = store.List().FirstOrDefault(s => s.Id == id)
                     ?? throw new StoreException("The previous server no longer exists; cannot undo.");
        try
        {
            RealmlistText.Validate(target.Realmlist);
        }
        catch (InvalidRealmlistException ex)
        {
            throw ex.ForServer(target.Name);
        }
        return target;
    }

    private Server? Resolve(State state, string? clientId = null)
    {
        // Legacy state.json files only carry a global active_id and an empty per-client map, so an unmapped
        // client still falls back to it. Once any per-client selection exists, a client without its own entry
        // has no active server instead of inheriting another client's global selection.
        var activeId = clientId is null
            ? state.ActiveId
            : state.ActiveByClient.TryGetValue(clientId, out var perClient)
                ? perClient
                : state.ActiveByClient.Count == 0 ? state.ActiveId : null;
        return activeId is null ? null : store.List().FirstOrDefault(s => s.Id == activeId);
    }

    private bool IsProjectionInSync(Server? active)
    {
        if (active is null)
            return !File.Exists(_projectionPath);
        try
        {
            return File.Exists(_projectionPath)
                   && PrivateStorage.ReadBounded(_projectionPath, PrivateStorage.MaxJsonBytes)
                       .AsSpan().SequenceEqual(ActiveProjection.Serialize(active));
        }
        catch (Exception ex) when (ex is IOException or InvalidRealmlistException or StoreException)
        {
            // An unreadable file, or a server whose realmlist is invalid, can never be "in sync".
            return false;
        }
    }

    private void DeleteProjection()
    {
        try
        {
            File.Delete(_projectionPath);
        }
        catch (IOException ex)
        {
            throw new StoreException($"Cannot delete '{_projectionPath}': {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StoreException($"Cannot delete '{_projectionPath}': {ex.Message}", ex);
        }
    }

    private State LoadState()
    {
        if (!File.Exists(_statePath))
            return new State { Version = FormatVersion };

        try
        {
            var state = JsonSerializer.Deserialize<State>(PrivateStorage.ReadBounded(_statePath, PrivateStorage.MaxJsonBytes), JsonOptions)
                        ?? throw new StoreException($"'{_statePath}' is empty.");
            return state.Version == FormatVersion
                ? state with
                {
                    ActiveByClient = state.ActiveByClient ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    UndoByClient = state.UndoByClient ?? new Dictionary<string, UndoRecord>(StringComparer.Ordinal),
                }
                : throw new StoreException($"'{_statePath}' has unsupported format version {state.Version}.");
        }
        catch (JsonException ex)
        {
            throw new StoreException($"'{_statePath}' is not valid: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new StoreException($"'{_statePath}' cannot be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new StoreException($"'{_statePath}' cannot be read: {ex.Message}", ex);
        }
    }

    private void SaveState(State state) =>
        writer.WriteAllBytes(_statePath, ActiveProjection.Utf8NoBom.GetBytes(JsonSerializer.Serialize(state, JsonOptions) + "\n"), PrivateStorage.PrivateFilePermissions);

    private sealed record State
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("active_id")] public string? ActiveId { get; init; }
        [JsonPropertyName("undo")] public UndoRecord? Undo { get; init; }
        [JsonPropertyName("active_by_client")] public Dictionary<string, string> ActiveByClient { get; init; } = new(StringComparer.Ordinal);
        [JsonPropertyName("undo_by_client")] public Dictionary<string, UndoRecord> UndoByClient { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed record UndoRecord
    {
        [JsonPropertyName("server_id")] public string? ServerId { get; init; }
    }
}
