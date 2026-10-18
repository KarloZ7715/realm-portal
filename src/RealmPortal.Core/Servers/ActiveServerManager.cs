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
    public Server? GetActive() => Resolve(LoadState());

    public ActiveStatus Status()
    {
        var state = LoadState();
        var active = Resolve(state);
        return new ActiveStatus(active, IsProjectionInSync(active), state.Undo is not null);
    }

    /// <summary>The <c>server_id</c> recorded for undo, or <c>null</c> when undo would deactivate; unset when there is nothing to undo.</summary>
    public string? PendingUndoServerId => LoadState().Undo?.ServerId;

    /// <summary>The server that <see cref="Undo"/> would restore, or <c>null</c> when undo would deactivate; <c>null</c> when there is nothing to undo.</summary>
    public Server? GetUndoTarget()
    {
        var state = LoadState();
        if (state.Undo is null)
            return null;
        return state.Undo.ServerId is { } id
            ? store.List().FirstOrDefault(s => s.Id == id)
            : null;
    }

    /// <summary>Clears the one-level undo record without changing the active server or projection.</summary>
    public void DiscardPendingUndo()
    {
        var state = LoadState();
        if (state.Undo is null)
            return;
        SaveState(state with { Undo = null });
    }

    /// <summary>
    /// Makes <paramref name="server"/> the active one and writes <c>activo.json</c>. Switching to the server that is
    /// already active rewrites the projection but keeps the undo information unchanged.
    /// </summary>
    /// <exception cref="StoreException">The server is not in the store.</exception>
    /// <exception cref="InvalidRealmlistException">The stored realmlist breaks the realmlist contract; nothing is written.</exception>
    public void Switch(Server server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var stored = store.List().FirstOrDefault(s => s.Id == server.Id)
                     ?? throw new StoreException($"Server '{server.Name}' is not in the store.");

        var state = LoadState();
        var previous = Resolve(state);

        try
        {
            RealmlistText.Validate(stored.Realmlist);
        }
        catch (InvalidRealmlistException ex)
        {
            throw ex.ForServer(stored.Name);
        }

        var next = previous?.Id == stored.Id
            ? state with { ActiveId = stored.Id }
            : new State { Version = FormatVersion, ActiveId = stored.Id, Undo = new UndoRecord { ServerId = previous?.Id } };
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

    /// <summary>
    /// Restores the server that was active before the last switch (or deactivates, deleting <c>activo.json</c>, when
    /// none was). One level only: a second undo without a switch in between fails.
    /// </summary>
    /// <returns>The restored server, or <c>null</c> when the result is "no active server".</returns>
    /// <exception cref="StoreException">There is nothing to undo, or the server to restore no longer exists.</exception>
    /// <exception cref="InvalidRealmlistException">The realmlist of the server to restore is invalid; nothing is changed.</exception>
    public Server? Undo()
    {
        var state = LoadState();
        if (state.Undo is null)
            throw new StoreException("Nothing to undo.");

        Server? target = null;
        if (state.Undo.ServerId is { } id)
        {
            target = store.List().FirstOrDefault(s => s.Id == id)
                     ?? throw new StoreException("The previous server no longer exists; cannot undo.");
        }

        if (target is not null)
        {
            try
            {
                RealmlistText.Validate(target.Realmlist);
            }
            catch (InvalidRealmlistException ex)
            {
                throw ex.ForServer(target.Name);
            }
        }

        var stateBeforeUndo = state;
        var nextState = new State { Version = FormatVersion, ActiveId = target?.Id, Undo = null };
        SaveState(nextState);
        try
        {
            if (target is null)
                DeleteProjection();
            else
                writer.WriteAllBytes(_projectionPath, ActiveProjection.Serialize(target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StoreException)
        {
            // DeleteProjection wraps I/O failures in StoreException. Roll state back so a failed
            // delete of activo.json does not commit ActiveId=null and drop the undo record.
            SaveState(stateBeforeUndo);
            throw;
        }

        return target;
    }

    /// <summary>Removes a server from the store; when it was the active one, deactivates it and deletes <c>activo.json</c>.</summary>
    /// <returns>False when the server does not exist.</returns>
    public bool Remove(string id)
    {
        if (!store.Remove(id))
            return false;

        var state = LoadState();
        if (state.ActiveId == id)
        {
            SaveState(new State { Version = FormatVersion, ActiveId = null, Undo = null });
            DeleteProjection();
        }
        else if (state.Undo?.ServerId == id)
        {
            SaveState(state with { Undo = null });
        }

        return true;
    }

    private Server? Resolve(State state) =>
        state.ActiveId is null ? null : store.List().FirstOrDefault(s => s.Id == state.ActiveId);

    private bool IsProjectionInSync(Server? active)
    {
        if (active is null)
            return !File.Exists(_projectionPath);
        try
        {
            return File.Exists(_projectionPath)
                   && File.ReadAllBytes(_projectionPath).AsSpan().SequenceEqual(ActiveProjection.Serialize(active));
        }
        catch (Exception ex) when (ex is IOException or InvalidRealmlistException)
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
            var state = JsonSerializer.Deserialize<State>(File.ReadAllBytes(_statePath), JsonOptions)
                        ?? throw new StoreException($"'{_statePath}' is empty.");
            return state.Version == FormatVersion
                ? state
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
        writer.WriteAllBytes(_statePath, ActiveProjection.Utf8NoBom.GetBytes(JsonSerializer.Serialize(state, JsonOptions) + "\n"));

    private sealed record State
    {
        [JsonPropertyName("version")] public int Version { get; init; }
        [JsonPropertyName("active_id")] public string? ActiveId { get; init; }
        [JsonPropertyName("undo")] public UndoRecord? Undo { get; init; }
    }

    private sealed record UndoRecord
    {
        [JsonPropertyName("server_id")] public string? ServerId { get; init; }
    }
}
