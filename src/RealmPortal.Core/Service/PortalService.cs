using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Servers;

namespace RealmPortal.Core.Service;

/// <summary>Application service: clients, servers, client files, active server and launch (see docs/app-service.md).</summary>
public sealed class PortalService : IPortalService
{
    private readonly string _configDirectory;
    private readonly string _projectionDirectory;

    /// <summary>Config directory this service was constructed with.</summary>
    public string ConfigDirectory => _configDirectory;
    private readonly IClientVersionReader _versionReader;
    private readonly IGameLaunchService _launchService;
    private readonly IGameProcessProbe _probe;
    private readonly Func<string, string?> _getVariable;
    private readonly IAtomicFileWriter _writer;
    private readonly ServerStore _servers;
    private readonly ClientStore _clients;
    private readonly ClientScanner _scanner;
    private readonly ClientScanOptions _scanOptions;
    private readonly ClientServerExtractor _serverExtractor = new();
    private readonly ActiveServerManager _active;
    private readonly LastSwitchJournal _journal;
    private readonly PortalSettingsStore _settings;
    private readonly NetworkSettingsStore _networkSettings;
    private readonly string _activeBackupRoot;
    private Exception? _storageSettingsError;

    public PortalService(
        string configDirectory,
        string projectionDirectory,
        IClientVersionReader versionReader,
        IGameLaunchService launchService,
        IGameProcessProbe? probe = null,
        Func<string, string?>? getVariable = null,
        IAtomicFileWriter? writer = null,
        ClientScanner? scanner = null,
        ClientScanOptions? scanOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionDirectory);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(launchService);
        _configDirectory = configDirectory;
        _projectionDirectory = projectionDirectory;
        _versionReader = versionReader;
        _scanner = scanner ?? new ClientScanner(versionReader);
        _scanOptions = scanOptions ?? new ClientScanOptions();
        _launchService = launchService;
        _probe = probe ?? new GameProcessProbe();
        _getVariable = getVariable ?? Environment.GetEnvironmentVariable;
        _writer = writer ?? new AtomicFileWriter();
        // One lock instance for every config store: one read-modify-write sequence at a time, also across processes.
        var directoryLock = new ConfigDirectoryLock(configDirectory);
        _servers = new ServerStore(configDirectory, _writer, directoryLock);
        _clients = new ClientStore(configDirectory, _writer, directoryLock);
        _active = new ActiveServerManager(_servers, configDirectory, projectionDirectory, _writer);
        _journal = new LastSwitchJournal(configDirectory, _writer, directoryLock);
        _settings = new PortalSettingsStore(configDirectory, _writer, _getVariable, directoryLock);
        _networkSettings = new NetworkSettingsStore(configDirectory, _writer, directoryLock);
        _activeBackupRoot = _settings.ResolveActiveBackupFolder(out _storageSettingsError);
        TryRecoverPendingStateUndo();
    }

    public ServiceResult<IReadOnlyList<ClientEntry>> ListClients()
    {
        try
        {
            return ServiceResult<IReadOnlyList<ClientEntry>>.Ok(_clients.List().Select(client => ToClientEntry(client)).ToList());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<ClientEntry>>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<ClientEntry> AddClient(string folder, string? name = null)
    {
        if (folder is null)
            throw new ArgumentNullException(nameof(folder));

        try
        {
            var version = ReadStoredClientVersion(folder);
            var stored = _clients.Add(folder, name);
            return ServiceResult<ClientEntry>.Ok(ToClientEntry(stored, version));
        }
        catch (Exception ex)
        {
            return ServiceResult<ClientEntry>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<ScanOutcome> Scan(ScanRequest? request = null, IProgress<ScanProgress>? progress = null)
    {
        request ??= new ScanRequest();
        try
        {
            var addedClients = new List<ClientEntry>();
            var importedServers = new List<ServerEntry>();
            var knownIds = _clients.List().Select(client => client.Id).ToHashSet(StringComparer.Ordinal);
            var scanOptions = _scanOptions with
            {
                KnownClientIds = knownIds,
                AdditionalRoots = [.. _scanOptions.AdditionalRoots, .. request.AdditionalRoots ?? []],
            };
            var latestProgress = new ClientScanProgress("", 0, 0);
            var scannerProgress = new InlineProgress<ClientScanProgress>(current =>
            {
                latestProgress = current;
                progress?.Report(new ScanProgress(current.CurrentSource, current.DirectoriesInspected, current.ClientsFound, importedServers.Count));
            });
            var discovered = _scanner.Scan(scanOptions, scannerProgress);
            foreach (var client in discovered)
            {
                // The scanner has already validated and canonicalized each candidate. Store registration must
                // preserve its discovered origin and avoid reading candidate files a second time.
                var stored = _clients.Add(client.Folder, name: null, origin: client.Origin);
                if (!knownIds.Contains(stored.Id))
                    addedClients.Add(ToClientEntry(stored, client.Version));
                if (request.ImportServers && !knownIds.Contains(stored.Id))
                {
                    var extracted = _serverExtractor.Extract(client.Folder, client.Build);
                    if (extracted is not null)
                    {
                        var requiredBuild = client.Build is > 0 ? client.Build : null;
                        var identity = extracted.Identity(requiredBuild);
                        var servers = _servers.List();
                        var matching = FindImportMatch(servers, identity);
                        if (matching is null)
                        {
                            var name = CreateImportedServerName(extracted, servers);
                            var created = Server.Create(name, extracted.Host, extracted.Realm, extracted.ExtraRealmlistLines,
                                requiredBuild, extracted.Account, clientId: client.Id);
                            _servers.Add(created);
                            matching = created;
                            importedServers.Add(ToServerEntry(created, _active.GetActive(client.Id)?.Id, client.Id, client.Version));
                        }
                        else if (matching.ClientId is null)
                        {
                            matching = matching with { ClientId = client.Id };
                            _servers.Update(matching);
                        }

                        _active.SetActiveForClient(matching, client.Id);
                    }
                }
                progress?.Report(new ScanProgress(latestProgress.CurrentSource, latestProgress.DirectoriesInspected,
                    latestProgress.ClientsFound, importedServers.Count));
            }

            var allClients = _clients.List().Select(client => ToClientEntry(client)).ToList();
            return ServiceResult<ScanOutcome>.Ok(new ScanOutcome(addedClients, importedServers, allClients));
        }
        catch (Exception ex)
        {
            return ServiceResult<ScanOutcome>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<ClientEntry> UpdateClient(UpdateClientRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var existing = _clients.FindById(request.ClientId);
            if (existing is null)
                return ServiceResult<ClientEntry>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
            if (TryCheckRunning(existing) is { } runningError)
                return ServiceResult<ClientEntry>.Fail(runningError);

            var newPath = request.Folder is null ? existing.Path : ClientPathSecurity.CanonicalizeTrustedFolder(request.Folder);
            var newId = ClientIds.FromFolderPath(newPath);
            ClientVersion? version = null;
            if (newId != existing.Id)
            {
                try
                {
                    if (_probe.IsGameRunning(newPath))
                        return ServiceResult<ClientEntry>.Fail(new ServiceError(ServiceErrorCode.GameRunning, Path: newPath));
                }
                catch (Exception ex)
                {
                    return ServiceResult<ClientEntry>.Fail(new ServiceError(ServiceErrorCode.ProcessCheckFailed, Path: newPath, Diagnostic: ex.Message));
                }

                try
                {
                    version = ReadStoredClientVersion(newPath);
                }
                catch (ClientVersionException ex)
                {
                    return ServiceResult<ClientEntry>.Fail(ServiceErrors.FromClientVersion(ex));
                }
                if (_clients.FindById(newId) is not null)
                    return ServiceResult<ClientEntry>.Fail(new ServiceError(ServiceErrorCode.ClientAlreadyExists, Path: newPath));
            }
            else
            {
                version = ReadVersionForEntry(existing.Path);
            }

            using (_servers.DirectoryLock.Acquire())
            using (CreateApplier().AcquireInstallationLock(existing.Id))
            using (newId == existing.Id ? null : CreateApplier().AcquireInstallationLock(newId))
            {
                var current = _clients.FindById(request.ClientId);
                if (current is null)
                    return ServiceResult<ClientEntry>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
                if (TryCheckRunning(current) is { } lockedRunning)
                    return ServiceResult<ClientEntry>.Fail(lockedRunning);
                if (newId != existing.Id)
                {
                    try
                    {
                        if (_probe.IsGameRunning(newPath))
                            return ServiceResult<ClientEntry>.Fail(new ServiceError(ServiceErrorCode.GameRunning, Path: newPath));
                    }
                    catch (Exception ex)
                    {
                        return ServiceResult<ClientEntry>.Fail(new ServiceError(ServiceErrorCode.ProcessCheckFailed, Path: newPath, Diagnostic: ex.Message));
                    }
                }

                var updated = new StoredClient(newId, newPath, request.Name, newId == existing.Id ? existing.Origin : ClientOrigin.Manual);
                _clients.ReplaceCore(existing.Id, updated);
                if (newId != existing.Id)
                {
                    _servers.RewriteClientIdReferencesCore(existing.Id, newId);
                    _active.MigrateClientState(existing.Id, newId);
                    if (_journal.Load(existing.Id) is not null)
                    {
                        _active.RemoveClientState(existing.Id, clearGlobalUndo: _journal.Load()?.ClientId == existing.Id);
                        _journal.Clear(existing.Id);
                    }
                }
                return ServiceResult<ClientEntry>.Ok(ToClientEntry(updated, version));
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<ClientEntry>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<Unit> RemoveClient(string clientId)
    {
        if (clientId is null)
            throw new ArgumentNullException(nameof(clientId));

        try
        {
            var client = _clients.FindById(clientId);
            if (client is null)
                return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));

            if (TryCheckRunning(client) is { } runningError)
                return ServiceResult<Unit>.Fail(runningError);

            using (_servers.DirectoryLock.Acquire())
            using (CreateApplier().AcquireInstallationLock(clientId))
            {
                if (TryCheckRunning(client) is { } lockedRunning)
                    return ServiceResult<Unit>.Fail(lockedRunning);
                var journal = _journal.Load(clientId);
                var globalJournal = _journal.Load();
                _servers.RewriteClientIdReferencesCore(clientId, null);
                _active.RemoveClientState(clientId, clearGlobalUndo: globalJournal?.ClientId == clientId);
                if (journal?.ClientId == clientId)
                    _journal.Clear(clientId);
                if (!_clients.RemoveCore(clientId))
                    return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
            }
            return ServiceResult<Unit>.Ok(default);
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<bool> IsGameRunning(string clientId)
    {
        if (clientId is null)
            throw new ArgumentNullException(nameof(clientId));

        try
        {
            var client = _clients.FindById(clientId)
                         ?? throw new StoreException($"No registered client with id '{clientId}'.");
            try
            {
                return ServiceResult<bool>.Ok(_probe.IsGameRunning(client.Path));
            }
            catch (Exception ex)
            {
                return ServiceResult<bool>.Fail(new ServiceError(ServiceErrorCode.ProcessCheckFailed, Path: client.Path, Diagnostic: ex.Message));
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<IReadOnlyList<ServerEntry>> ListServers(string? clientId = null, bool compatibleOnly = false)
    {
        try
        {
            ClientVersion? clientVersion = null;
            if (clientId is not null)
            {
                var client = _clients.FindById(clientId)
                             ?? throw new StoreException($"No registered client with id '{clientId}'.");
                try
                {
                    clientVersion = ReadStoredClientVersion(client.Path);
                }
                catch (Exception ex) when (ex is ClientVersionException or StoreException)
                {
                    clientVersion = null;
                }
            }

            var activeId = _active.GetActive(clientId)?.Id;
            var rows = _servers.List()
                .Select(s => ToServerEntry(s, activeId, clientId, clientVersion))
                .Where(e => !compatibleOnly || e.IsCompatible)
                .ToList();
            return ServiceResult<IReadOnlyList<ServerEntry>>.Ok(rows);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<ServerEntry>>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<ServerEntry> AddServer(NewServer server)
    {
        if (server is null)
            throw new ArgumentNullException(nameof(server));

        try
        {
            if (server.ClientId is not null && _clients.FindById(server.ClientId) is null)
                return ServiceResult<ServerEntry>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));

            List<string>? extra = null;
            if (server.ExtraRealmlistLines is { Count: > 0 })
                extra = server.ExtraRealmlistLines.ToList();
            else if (server.PatchlistHost is not null)
                extra = [$"set patchlist {server.PatchlistHost}"];
            var created = Server.Create(
                server.Name,
                server.Host,
                server.Realm,
                extra,
                server.RequiredClientBuild,
                server.Account,
                clientId: server.ClientId,
                clearCacheOnSwitch: server.ClearCacheOnSwitch);
            _servers.Add(created);
            var activeId = _active.GetActive(server.ClientId)?.Id;
            return ServiceResult<ServerEntry>.Ok(ToServerEntry(created, activeId, server.ClientId, null));
        }
        catch (Exception ex)
        {
            return ServiceResult<ServerEntry>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<ServerEntry> UpdateServer(UpdateServerRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            using var configLock = _servers.DirectoryLock.Acquire();
            var existing = _servers.List().FirstOrDefault(s => s.Id == request.ServerId);
            if (existing is null)
                return ServiceResult<ServerEntry>.Fail(new ServiceError(ServiceErrorCode.ServerNotFound));
            if (request.ClientId is not null && _clients.FindById(request.ClientId) is null)
                return ServiceResult<ServerEntry>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));

            // Fields the request leaves null keep their stored values (#93): the extra realmlist lines (patchlist, ...)
            // when both ExtraRealmlistLines and PatchlistHost are null (an empty list clears them), and the required
            // client build when RequiredClientBuild is null (ClearRequiredClientBuild removes it).
            IEnumerable<string>? extra = request.ExtraRealmlistLines;
            if (extra is null && request.PatchlistHost is not null)
                extra = [$"set patchlist {request.PatchlistHost}"];
            extra ??= StoredExtraRealmlistLines(existing);
            if (request.ClearRequiredClientBuild && request.RequiredClientBuild is not null)
                throw new ArgumentException("Set either a client build or clear it, not both.", "requiredClientBuild");
            var build = request.ClearRequiredClientBuild ? null : request.RequiredClientBuild ?? existing.RequiredClientBuild;
            var updated = Server.Create(
                request.Name,
                request.Host,
                request.Realm,
                extra,
                build,
                request.Account,
                request.ServerId,
                request.ClientId,
                clearCacheOnSwitch: request.ClearCacheOnSwitch);

            if (existing.Realmlist != updated.Realmlist
                || existing.Realm != updated.Realm
                || existing.RequiredClientBuild != updated.RequiredClientBuild)
            {
                var affectedClientIds = _active.ClientIdsActiveOn(existing.Id).ToHashSet(StringComparer.Ordinal);
                if (_active.GetActive()?.Id == existing.Id && _journal.Load()?.ClientId is { } journalClient)
                    affectedClientIds.Add(journalClient);
                using var installationLocks = AcquireInstallationLocks(affectedClientIds);
                foreach (var clientId in affectedClientIds)
                {
                    var client = _clients.FindById(clientId);
                    if (client is null)
                        continue;
                    if (TryCheckRunning(client) is { } runningError)
                        return ServiceResult<ServerEntry>.Fail(runningError with { ServerName = existing.Name });
                }
            }

            _active.Update(updated);
            var activeId = _active.GetActive(updated.ClientId)?.Id;
            ClientVersion? clientVersion = null;
            if (updated.ClientId is { } preferredId && _clients.FindById(preferredId) is { } preferred)
                clientVersion = ReadVersionForEntry(preferred.Path);
            return ServiceResult<ServerEntry>.Ok(ToServerEntry(updated, activeId, updated.ClientId, clientVersion));
        }
        catch (Exception ex)
        {
            return ServiceResult<ServerEntry>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<Unit> RemoveServer(string serverId)
    {
        if (serverId is null)
            throw new ArgumentNullException(nameof(serverId));

        try
        {
            using var configLock = _servers.DirectoryLock.Acquire();
            var server = _servers.List().FirstOrDefault(s => s.Id == serverId);
            if (server is null)
                return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.ServerNotFound));
            var active = _active.GetActive();
            var pendingUndoServerId = _active.PendingUndoServerId;
            var clientIds = _active.ClientIdsActiveOn(serverId).ToHashSet(StringComparer.Ordinal);
            var undoClientIds = _active.ClientIdsWithUndoTo(serverId).ToHashSet(StringComparer.Ordinal);
            if (active?.Id == serverId && _journal.Load()?.ClientId is { } journalClient)
                clientIds.Add(journalClient);
            using var installationLocks = AcquireInstallationLocks(clientIds);
            foreach (var clientId in clientIds)
            {
                var client = _clients.FindById(clientId);
                if (client is null)
                    continue;
                if (TryCheckRunning(client) is { } runningError)
                    return ServiceResult<Unit>.Fail(runningError with { ServerName = server.Name });
            }

            if (!_active.Remove(serverId))
                return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.ServerNotFound));
            var currentJournal = _journal.Load();
            foreach (var clientId in clientIds.Concat(undoClientIds).Distinct(StringComparer.Ordinal))
                if (_journal.Load(clientId) is not null)
                    _journal.Clear(clientId);
            if (active?.Id == serverId && currentJournal?.ClientId is null)
                _journal.Clear();
            else if (pendingUndoServerId == serverId && currentJournal?.ClientId is null)
                _journal.Clear();
            return ServiceResult<Unit>.Ok(default);
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<SwitchOutcome> SwitchServer(SwitchRequest request, IProgress<ServiceProgress>? progress = null) =>
        SwitchCore(request, progress, launch: false);

    public ServiceResult<SwitchOutcome> Play(SwitchRequest request, IProgress<ServiceProgress>? progress = null) =>
        SwitchCore(request, progress, launch: true);

    /// <summary>
    /// Marks a server active without touching client files. Used by the CLI when no client folder is set
    /// (compatibility is not checked against a real client).
    /// </summary>
    public ServiceResult<ServerEntry> ActivateWithoutClientFiles(string serverId)
    {
        if (serverId is null)
            throw new ArgumentNullException(nameof(serverId));

        try
        {
            // One critical section: the snapshot of the active server, the activation and the journal
            // save must describe the same switch.
            using (_servers.DirectoryLock.Acquire())
            {
                var server = _servers.List().FirstOrDefault(s => s.Id == serverId)
                             ?? throw new StoreException($"No server matches '{serverId}'.");
                var before = _active.GetActive();
                _active.Switch(server);
                if (before?.Id != server.Id)
                    _journal.Save(new LastSwitchEntry(null, null));
                return ServiceResult<ServerEntry>.Ok(ToServerEntry(server, server.Id, null, null));
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<ServerEntry>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<UndoOutcome> Undo(string? clientId = null)
    {
        try
        {
            // One critical section from the CanUndo check through _journal.Clear(): the client-file
            // undo, the journal flag and the active-state restore must all describe the same switch.
            using (_servers.DirectoryLock.Acquire())
            {
                if (!_active.HasPendingUndo(clientId))
                    return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.NothingToUndo));

                // A pending undo in state.json without a matching journal entry cannot restore client files.
                var journal = _journal.Load(clientId);
                if (journal is null)
                    return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.NothingToUndo));
                var globalJournalClientId = clientId is null ? journal.ClientId : _journal.Load()?.ClientId;

                StoredClient? client = journal.ClientId is null ? null : _clients.FindById(journal.ClientId);
                using var installationLock = client is null ? null : CreateApplier().AcquireInstallationLock(client.Id);
                var applier = CreateApplier();
                if (client is not null)
                {
                    if (TryCheckRunning(client) is { } runningError)
                        return ServiceResult<UndoOutcome>.Fail(runningError);
                    // ClientFilesApplier acquires the same installation lock itself and checks the probe
                    // again immediately before file restoration.
                    installationLock?.Dispose();
                }

                if (_active.GetPendingUndoServerId(clientId) is { } missingId
                    && _servers.List().All(s => s.Id != missingId))
                {
                    return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.UndoTargetMissing));
                }

                var undoTarget = _active.GetUndoTarget(clientId);
                if (undoTarget is not null)
                {
                    try
                    {
                        RealmlistText.Validate(undoTarget.Realmlist);
                    }
                    catch (InvalidRealmlistException ex)
                    {
                        return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromException(ex.ForServer(undoTarget.Name)));
                    }
                }

                IReadOnlyList<UndoFileResult> fileResults = [];
                if (!journal.FilesUndone && journal.BackupId is not null && client is not null)
                {
                    try
                    {
                        ClientPathSecurity.EnsureTrustedDirectoryPath(client.Path);
                        var undoFiles = applier.Undo(
                            journal.ClientId!,
                            client.Path,
                            journal.BackupId,
                            markUndone: false,
                            expectedManifestSha256: journal.ManifestSha256);
                        fileResults = undoFiles.Files;
                        if (undoFiles.HasConflicts || !string.Equals(undoFiles.BackupId, journal.BackupId, StringComparison.Ordinal))
                        {
                            var conflicts = undoFiles.Files
                                .Where(f => f.Status == UndoFileStatus.Conflict)
                                .Select(f => f.RelativePath.Replace('\\', '/'))
                                .ToList();
                            return ServiceResult<UndoOutcome>.Fail(ServiceErrors.UndoConflict(conflicts));
                        }

                        _journal.MarkFilesUndone(clientId);
                    }
                    catch (ClientFilesException ex)
                    {
                        return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromClientFiles(ex));
                    }
                }

                Server? restored;
                try
                {
                    restored = _active.Undo(clientId, globalJournalClientId);
                }
                catch (Exception ex)
                {
                    // State-only undo (no backup): ActiveServerManager already restored state.json, and no client files
                    // were touched. Report the original failure as Unchanged instead of a synthetic inconsistent state.
                    if (client is null || journal.ClientId is null)
                        return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromException(ex, Consistency.Unchanged));

                    try
                    {
                        var active = _active.GetActive(clientId)
                                     ?? throw new InvalidOperationException("No active server to re-apply after a failed undo.");
                        var version = ReadStoredClientVersion(client.Path);
                        var plan = applier.Plan(BuildRequest(journal.ClientId, active, client.Path, version));
                        if (!plan.IsEmpty)
                        {
                            var applied = applier.Apply(plan);
                            if (applied.BackupId is { } rollbackBackupId)
                                applier.MarkBackupUndone(journal.ClientId, rollbackBackupId);
                        }

                        // Original backup stays pending; the client again holds its sha256_after bytes.
                        _journal.MarkFilesNotUndone(clientId);
                    }
                    catch (Exception rollbackEx)
                    {
                        return ServiceResult<UndoOutcome>.Fail(ServiceErrors.Inconsistent(ServiceErrors.FromException(rollbackEx, Consistency.Inconsistent)));
                    }

                    return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromException(ex, Consistency.RolledBack));
                }

                if (journal.BackupId is not null && journal.ClientId is not null)
                    applier.MarkBackupUndone(journal.ClientId, journal.BackupId);

                _journal.Clear(clientId);
                var entry = restored is null ? null : ToServerEntry(restored, restored.Id, journal.ClientId, null);
                var notice = BuildUndoClientFilesNotice(restored, fileResults, client);
                return ServiceResult<UndoOutcome>.Ok(new UndoOutcome(entry, fileResults, notice));
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<PortalStatus> DiscardUndo(string? clientId = null)
    {
        try
        {
            using (_servers.DirectoryLock.Acquire())
            {
                // Journal top-level ClientId can lag behind state.Undo after Clear promotes a survivor.
                var ownsGlobal = clientId is not null
                    && _active.HasPendingUndo(null)
                    && string.Equals(_journal.Load()?.ClientId, clientId, StringComparison.Ordinal);
                _active.DiscardPendingUndo(clientId);
                if (ownsGlobal)
                    _active.DiscardPendingUndo(null);
                _journal.Clear(clientId);
                return GetStatus();
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<PortalStatus>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<IReadOnlyList<BackupEntry>> ListBackups(string? clientId = null)
    {
        try
        {
            var clients = _clients.List();
            if (clientId is not null && clients.All(client => client.Id != clientId))
                return ServiceResult<IReadOnlyList<BackupEntry>>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));

            var known = clients.ToDictionary(client => client.Id, StringComparer.Ordinal);
            var servers = _servers.List().ToDictionary(server => server.Id, server => server.Name, StringComparer.Ordinal);
            var backups = CreateApplier().ListBackups(clientId)
                .Where(backup => known.ContainsKey(backup.ClientId))
                .Select(backup => EnrichBackup(backup, known[backup.ClientId].Path, servers))
                .ToArray();
            return ServiceResult<IReadOnlyList<BackupEntry>>.Ok(backups);
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<BackupEntry>>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<BackupEntry> CreateBackup(string clientId, string? label = null)
    {
        try
        {
            var client = _clients.FindById(clientId);
            if (client is null)
                return ServiceResult<BackupEntry>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
            if (TryCheckRunning(client) is { } runningError)
                return ServiceResult<BackupEntry>.Fail(runningError);

            ClientPathSecurity.EnsureTrustedDirectoryPath(client.Path);
            var entry = CreateApplier().CreateBackup(client.Id, client.Path, label);
            var servers = _servers.List().ToDictionary(server => server.Id, server => server.Name, StringComparer.Ordinal);
            return ServiceResult<BackupEntry>.Ok(EnrichBackup(entry, client.Path, servers));
        }
        catch (Exception ex)
        {
            return ServiceResult<BackupEntry>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<RestoreBackupOutcome> RestoreBackup(string clientId, string backupId, bool force = false)
    {
        try
        {
            var client = _clients.FindById(clientId);
            if (client is null)
                return ServiceResult<RestoreBackupOutcome>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
            if (TryCheckRunning(client) is { } runningError)
                return ServiceResult<RestoreBackupOutcome>.Fail(runningError);

            using (_servers.DirectoryLock.Acquire())
            {
                var journal = _journal.Load(clientId);
                var globalJournal = _journal.Load();
                if (journal?.BackupId == backupId
                    && _active.GetPendingUndoServerId(clientId) is { } missingId
                    && _servers.List().All(server => server.Id != missingId))
                    return ServiceResult<RestoreBackupOutcome>.Fail(new ServiceError(ServiceErrorCode.UndoTargetMissing));

                ClientPathSecurity.EnsureTrustedDirectoryPath(client.Path);
                RestoreBackupOutcome outcome;
                try
                {
                    outcome = CreateApplier().RestoreBackup(clientId, client.Path, backupId, force);
                }
                catch (ClientFilesException ex) when (ex.Code == ClientFilesErrorCode.UndoConflict)
                {
                    var path = Path.GetFullPath(ex.Path);
                    var relative = Path.GetRelativePath(client.Path, path).Replace('\\', '/');
                    return ServiceResult<RestoreBackupOutcome>.Fail(ServiceErrors.UndoConflict([relative]));
                }
                catch (ClientFilesException ex) when (ex.Code == ClientFilesErrorCode.PathNotFound)
                {
                    return ServiceResult<RestoreBackupOutcome>.Fail(new ServiceError(ServiceErrorCode.BackupNotFound, Path: ex.Path, Diagnostic: ex.Message));
                }

                if (journal?.BackupId is not null && _active.HasPendingUndo(clientId))
                {
                    if (journal.BackupId == backupId)
                        _active.Undo(clientId, globalJournal?.ClientId);
                    else
                    {
                        var ownsGlobal = _active.HasPendingUndo(null)
                            && string.Equals(globalJournal?.ClientId, clientId, StringComparison.Ordinal);
                        _active.DiscardPendingUndo(clientId);
                        if (ownsGlobal)
                            _active.DiscardPendingUndo(null);
                    }
                    _journal.Clear(clientId);
                }

                return ServiceResult<RestoreBackupOutcome>.Ok(outcome);
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<RestoreBackupOutcome>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<Unit> DeleteBackup(string clientId, string backupId)
    {
        try
        {
            var client = _clients.FindById(clientId);
            if (client is null)
                return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
            if (TryCheckRunning(client) is { } runningError)
                return ServiceResult<Unit>.Fail(runningError);

            using (_servers.DirectoryLock.Acquire())
            {
                var journal = _journal.Load(clientId);
                var globalJournal = _journal.Load();
                try
                {
                    CreateApplier().DeleteBackup(clientId, backupId);
                }
                catch (ClientFilesException ex) when (ex.Code == ClientFilesErrorCode.PathNotFound)
                {
                    return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.BackupNotFound, Path: ex.Path, Diagnostic: ex.Message));
                }

                if (journal?.BackupId == backupId)
                {
                    var ownsGlobal = _active.HasPendingUndo(null)
                        && string.Equals(globalJournal?.ClientId, clientId, StringComparison.Ordinal);
                    _active.DiscardPendingUndo(clientId);
                    if (ownsGlobal)
                        _active.DiscardPendingUndo(null);
                    _journal.Clear(clientId);
                }
                return ServiceResult<Unit>.Ok(default);
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<PortalStatus> GetStatus()
    {
        try
        {
            var status = _active.Status();
            var active = status.Active is null ? null : ToServerEntry(status.Active, status.Active.Id, null, null);
            var canUndo = ComputeCanUndo();
            var drifting = status.Active is null
                ? []
                : ListClientsNotMatchingActive(status.Active);
            return ServiceResult<PortalStatus>.Ok(
                new PortalStatus(active, canUndo, status.ProjectionInSync, _activeBackupRoot, drifting,
                    _clients.List().Select(client => (client.Id, Active: _active.GetActive(client.Id)))
                        .Where(pair => pair.Active is not null)
                        .ToDictionary(pair => pair.Id,
                            pair => ToServerEntry(pair.Active!, pair.Active!.Id, pair.Id,
                                ReadVersionForEntry(_clients.FindById(pair.Id)?.Path ?? string.Empty)),
                            StringComparer.Ordinal)));
        }
        catch (Exception ex)
        {
            return ServiceResult<PortalStatus>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<LauncherSettings> GetLauncherSettings()
    {
        try
        {
            return ServiceResult<LauncherSettings>.Ok(_settings.GetLauncherSettings());
        }
        catch (Exception ex)
        {
            return ServiceResult<LauncherSettings>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<StorageSettings> GetStorageSettings()
    {
        if (_storageSettingsError is not null)
            return ServiceResult<StorageSettings>.Fail(ServiceErrors.FromException(_storageSettingsError));

        try
        {
            return ServiceResult<StorageSettings>.Ok(_settings.GetStorageSettings());
        }
        catch (Exception ex)
        {
            return ServiceResult<StorageSettings>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<Unit> SetStorageSettings(StorageSettings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        try
        {
            _settings.SetStorageSettings(settings);
            _storageSettingsError = null;
            return ServiceResult<Unit>.Ok(default);
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<NetworkSettings> GetNetworkSettings()
    {
        try
        {
            // Startup-safe: missing/corrupt JSON → defaults; unsupported version / IO → fail.
            var settings = _networkSettings.LoadOrDefault(out var error);
            if (error is not null && !NetworkSettingsStore.IsCorruptContent(error))
                return ServiceResult<NetworkSettings>.Fail(ServiceErrors.FromException(error));
            return ServiceResult<NetworkSettings>.Ok(settings);
        }
        catch (Exception ex)
        {
            return ServiceResult<NetworkSettings>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<Unit> SetNetworkSettings(NetworkSettings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        try
        {
            // Full snapshot write. Field tweaks that must not clobber concurrent ETag/LastAttempt
            // must use UpdateNetworkSettings / NetworkSettingsStore.Update instead.
            _networkSettings.Set(settings);
            return ServiceResult<Unit>.Ok(default);
        }
        catch (Exception ex) when (NetworkSettingsStore.IsCorruptContent(ex))
        {
            // Set re-reads and cannot repair; Overwrite moves the bad file aside first.
            try
            {
                _networkSettings.Overwrite(settings);
                return ServiceResult<Unit>.Ok(default);
            }
            catch (Exception overwriteEx)
            {
                return ServiceResult<Unit>.Fail(ServiceErrors.FromException(overwriteEx));
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<NetworkSettings> UpdateNetworkSettings(Func<NetworkSettings, NetworkSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        try
        {
            return ServiceResult<NetworkSettings>.Ok(_networkSettings.Update(mutate));
        }
        catch (Exception ex) when (NetworkSettingsStore.IsCorruptContent(ex))
        {
            // Same backup-and-repair path as SetNetworkSettings: move corrupt aside, then apply mutate to defaults.
            try
            {
                var repaired = mutate(NetworkSettings.Default)
                    ?? throw new InvalidOperationException("mutate returned null.");
                _networkSettings.Overwrite(repaired);
                return ServiceResult<NetworkSettings>.Ok(repaired);
            }
            catch (InvalidOperationException overwriteEx)
                when (overwriteEx.Message.Contains("no longer corrupt", StringComparison.Ordinal))
            {
                // Another caller repaired the file first — apply our mutation to the repaired content.
                try
                {
                    return ServiceResult<NetworkSettings>.Ok(_networkSettings.Update(mutate));
                }
                catch (Exception retryEx)
                {
                    return ServiceResult<NetworkSettings>.Fail(ServiceErrors.FromException(retryEx));
                }
            }
            catch (Exception overwriteEx)
            {
                return ServiceResult<NetworkSettings>.Fail(ServiceErrors.FromException(overwriteEx));
            }
        }
        catch (Exception ex)
        {
            return ServiceResult<NetworkSettings>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<Unit> SetLauncherSettings(LauncherSettings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        try
        {
            _settings.SetLauncherSettings(settings);
            return ServiceResult<Unit>.Ok(default);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.InvalidLauncherSettings, Diagnostic: ex.Message));
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.FromException(ex));
        }
    }

    private ServiceResult<SwitchOutcome> SwitchCore(SwitchRequest request, IProgress<ServiceProgress>? progress, bool launch)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var client = _clients.FindById(request.ClientId)
                         ?? throw new StoreException($"No registered client with id '{request.ClientId}'.");
            var server = _servers.List().FirstOrDefault(s => s.Id == request.ServerId)
                         ?? throw new StoreException($"No server matches '{request.ServerId}'.");

            ClientVersion version;
            try
            {
                version = ReadStoredClientVersion(client.Path);
            }
            catch (ClientVersionException ex)
            {
                return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.FromClientVersion(ex));
            }

            if (!request.Force && server.RequiredClientBuild is { } required && version.Build != required)
                return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.Incompatible(required, version.Build));

            try
            {
                RealmlistText.Validate(server.Realmlist);
            }
            catch (InvalidRealmlistException ex)
            {
                return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.FromException(ex.ForServer(server.Name)));
            }

            SwitchOutcome outcome;
            // Hold the installation lock even when the plan is empty: the running-process check
            // and every state/file mutation for this client must be serialized.
            using (_servers.DirectoryLock.Acquire())
            {
                client = _clients.FindById(request.ClientId)
                         ?? throw new StoreException($"No registered client with id '{request.ClientId}'.");
                server = _servers.List().FirstOrDefault(s => s.Id == request.ServerId)
                         ?? throw new StoreException($"No server matches '{request.ServerId}'.");
                try
                {
                    version = ReadStoredClientVersion(client.Path);
                }
                catch (ClientVersionException ex)
                {
                    return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.FromClientVersion(ex));
                }
                if (!request.Force && server.RequiredClientBuild is { } currentRequired && version.Build != currentRequired)
                    return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.Incompatible(currentRequired, version.Build));
                try
                {
                    RealmlistText.Validate(server.Realmlist);
                }
                catch (InvalidRealmlistException ex)
                {
                    return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.FromException(ex.ForServer(server.Name)));
                }

                var applier = CreateApplier();
                using var installationLock = applier.AcquireInstallationLock(request.ClientId);
                if (TryCheckRunning(client) is { } runningError)
                    return ServiceResult<SwitchOutcome>.Fail(runningError with { Consistency = Consistency.Unchanged });

                var active = _active.GetActive(request.ClientId);
                var sameServerActive = active?.Id == server.Id;
                var previousEntry = active is null ? null : ToServerEntry(active, active.Id, request.ClientId, version);

                ClientFilesPlan plan = null!;
                if (!RunStep(progress, ServiceStep.Plan, () => plan = applier.Plan(BuildRequest(request.ClientId, server, client.Path, version)), out var planError))
                    return ServiceResult<SwitchOutcome>.Fail(planError!);

                string? backupId = null;
                string? manifestSha256 = null;
                IReadOnlyList<string> changedFiles = [];
                progress?.Report(new ServiceProgress(ServiceStep.WriteFiles, StepState.Started));
                try
                {
                    if (!plan.IsEmpty)
                    {
                        var applied = applier.Apply(plan, installationLock);
                        backupId = applied.BackupId;
                        manifestSha256 = applied.ManifestSha256;
                        changedFiles = applied.Edits.Select(e => e.RelativePath.Replace('\\', '/')).ToList();
                    }

                    progress?.Report(new ServiceProgress(ServiceStep.WriteFiles, StepState.Completed));
                }
                catch (Exception ex)
                {
                    var error = ServiceErrors.FromException(ex, Consistency.Unchanged);
                    progress?.Report(new ServiceProgress(ServiceStep.WriteFiles, StepState.Failed, error));
                    return ServiceResult<SwitchOutcome>.Fail(error);
                }

                try
                {
                    if (!RunStep(progress, ServiceStep.Activate, () => _active.Switch(server, request.ClientId), out var activateError))
                    {
                        if (backupId is not null)
                        {
                            var rollback = RollbackSwitchApply(applier, request.ClientId, client.Path, backupId, installationLock, manifestSha256);
                            if (!rollback.Succeeded)
                            {
                                return ServiceResult<SwitchOutcome>.Fail(rollback.Error!);
                            }
                        }

                        return ServiceResult<SwitchOutcome>.Fail(activateError! with
                        {
                            Consistency = backupId is not null ? Consistency.RolledBack : Consistency.Unchanged,
                        });
                    }
                }
                catch
                {
                    throw;
                }

                if (!sameServerActive)
                    _journal.Save(new LastSwitchEntry(request.ClientId, backupId, ManifestSha256: manifestSha256));

                var activeEntry = ToServerEntry(server, server.Id, request.ClientId, version);
                var previous = sameServerActive ? activeEntry : previousEntry;

                // Clear Cache/WDB only after a successful apply+activate that targets another server (or first
                // activation). Fail-soft: a clear failure must not roll back the switch (#106).
                string? cacheClearWarning = null;
                if (!sameServerActive && ShouldClearCacheOnSwitch(server))
                {
                    try
                    {
                        ClientCacheClearer.Clear(client.Path);
                    }
                    catch (Exception ex) when (ex is ClientFilesException or IOException or UnauthorizedAccessException)
                    {
                        cacheClearWarning = ex is ClientFilesException cfe
                            ? cfe.Message
                            : $"Could not clear Cache/WDB: {ex.Message}";
                    }
                }

                outcome = new SwitchOutcome(activeEntry, previous, backupId, changedFiles, cacheClearWarning);
            }

            if (launch)
            {
                if (!RunStep(progress, ServiceStep.Launch, () => LaunchGame(client.Path, _settings.GetLauncherSettings()), out var launchError))
                {
                    // A setting the launcher cannot apply is not a process-start failure. The switch is already applied.
                    if (launchError!.Code == ServiceErrorCode.InvalidLauncherSettings)
                        return ServiceResult<SwitchOutcome>.Fail(launchError with { Consistency = Consistency.AppliedLaunchFailed });

                    return ServiceResult<SwitchOutcome>.Fail(launchError with { Code = ServiceErrorCode.LaunchFailed, Consistency = Consistency.AppliedLaunchFailed });
                }
            }

            return ServiceResult<SwitchOutcome>.Ok(outcome);
        }
        catch (Exception ex)
        {
            return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.FromException(ex));
        }
    }

    private void LaunchGame(string clientPath, LauncherSettings settings)
    {
        var executable = GameExecutable.Find(clientPath)
                         ?? throw new InvalidOperationException($"{GameExecutable.FileName} not found in '{clientPath}'.");
        try
        {
            _launchService.Launch(settings.ToSpec(executable, clientPath));
        }
        catch (ArgumentException ex) when (ex.ParamName == GameLaunchSpec.UnsupportedSettingParameter)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static bool RunStep(IProgress<ServiceProgress>? progress, ServiceStep step, Action work, out ServiceError? error)
    {
        progress?.Report(new ServiceProgress(step, StepState.Started));
        try
        {
            work();
            progress?.Report(new ServiceProgress(step, StepState.Completed));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ServiceErrors.FromException(ex, Consistency.Unchanged);
            progress?.Report(new ServiceProgress(step, StepState.Failed, error));
            return false;
        }
    }

    private IClientFilesApplier CreateApplier() =>
        new ClientFilesApplier(new ClientFilesPlanner(), _writer, _probe, _activeBackupRoot);

    private static ClientFilesRequest BuildRequest(string installationId, Server server, string clientRoot, ClientVersion version) =>
        new(installationId, server.Id, clientRoot, version, server.Realmlist, server.Realm);

    /// <summary>
    /// Version read for a stored client. Symlink and reparse checks run here; a missing folder stays a
    /// <see cref="ClientVersionException"/> so list and status can report that one client as unavailable.
    /// </summary>
    private ClientVersion ReadStoredClientVersion(string path)
    {
        ClientPathSecurity.EnsureTrustedDirectoryPath(path);
        return _versionReader.Read(path);
    }

    private ClientVersion? ReadVersionForEntry(string path)
    {
        try
        {
            return ReadStoredClientVersion(path);
        }
        catch (ClientVersionException)
        {
            return null;
        }
        catch (StoreException)
        {
            return null;
        }
    }

    private ServiceError? TryCheckRunning(StoredClient client)
    {
        try
        {
            return _probe.IsGameRunning(client.Path)
                ? new ServiceError(ServiceErrorCode.GameRunning, Path: client.Path)
                : null;
        }
        catch (Exception ex)
        {
            return new ServiceError(ServiceErrorCode.ProcessCheckFailed, Path: client.Path, Diagnostic: ex.Message);
        }
    }

    private IDisposable AcquireInstallationLocks(IEnumerable<string> clientIds)
    {
        var applier = CreateApplier();
        var acquired = new List<InstallationLock>();
        try
        {
            foreach (var clientId in clientIds.OrderBy(id => id, StringComparer.Ordinal))
                acquired.Add(applier.AcquireInstallationLock(clientId));
            return new InstallationLockSet(acquired);
        }
        catch
        {
            for (var i = acquired.Count - 1; i >= 0; i--)
                acquired[i].Dispose();
            throw;
        }
    }

    private sealed class InstallationLockSet(List<InstallationLock> locks) : IDisposable
    {
        public void Dispose()
        {
            for (var i = locks.Count - 1; i >= 0; i--)
                locks[i].Dispose();
        }
    }

    /// <summary>
    /// The stored server an extracted one is the same as: an exact <see cref="RealmlistIdentity"/> match first, otherwise
    /// a server with the same host and realm and no required build (a client-independent entry matches any build, #93).
    /// </summary>
    internal static Server? FindImportMatch(IReadOnlyList<Server> servers, RealmlistIdentity extracted) =>
        servers.FirstOrDefault(server => RealmlistIdentity.FromServer(server).Equals(extracted))
        ?? servers.FirstOrDefault(server => server.RequiredClientBuild is null
            && RealmlistIdentity.Create(server.Host, server.Realm, extracted.RequiredClientBuild).Equals(extracted));

    /// <summary>The stored realmlist lines after the first <c>set realmlist</c> line (e.g. <c>set patchlist …</c>).</summary>
    private static string[] StoredExtraRealmlistLines(Server server) =>
        server.Realmlist.Split(RealmlistText.LineEnding, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();

    /// <summary>
    /// Name of a server imported from a client (#98): the realm (control characters stripped, trimmed) when there is one,
    /// otherwise the host. The card already shows the host under the name, so it is not repeated. Capped at
    /// <see cref="Server.MaxNameLength"/> and made unique case-insensitively with <c>" #2"</c>, <c>" #3"</c>, ...
    /// </summary>
    internal static string CreateImportedServerName(ExtractedServer extracted, IReadOnlyList<Server> existing)
    {
        var realm = new string((extracted.Realm ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        var basis = realm.Length > 0 ? realm : extracted.Host;
        basis = basis.Trim();
        basis = TruncateToTextElements(basis, Server.MaxNameLength).TrimEnd();
        if (basis.Length == 0)
            basis = "Imported server";

        var candidate = basis;
        var suffix = 2;
        while (existing.Any(server => string.Equals(server.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            var tail = $" #{suffix++}";
            var prefix = TruncateToTextElements(basis, Server.MaxNameLength - tail.Length).TrimEnd();
            candidate = (prefix.Length > 0 ? prefix : "Imported server") + tail;
        }
        return candidate;
    }

    /// <summary>
    /// The longest prefix of <paramref name="value"/> made of whole text elements (grapheme clusters, see
    /// <see cref="System.Globalization.StringInfo"/>) whose length is at most <paramref name="maxLength"/> UTF-16 chars,
    /// the unit <see cref="Server.MaxNameLength"/> is measured in. A surrogate pair, a base character with its combining
    /// marks, or a joined emoji sequence is never split.
    /// </summary>
    internal static string TruncateToTextElements(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        var end = 0;
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            var next = elements.ElementIndex + ((string)elements.Current).Length;
            if (next > maxLength)
                break;
            end = next;
        }
        return value[..end];
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>
    /// Name shown for a client the user has not named: its folder name. It never embeds the version, which the UI
    /// shows next to it from <see cref="ClientVersion.DisplayVersion"/>.
    /// </summary>
    internal static string DefaultClientName(string clientPath)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(clientPath);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private ClientEntry ToClientEntry(StoredClient stored, ClientVersion? knownVersion = null)
    {
        try
        {
            var version = knownVersion ?? ReadStoredClientVersion(stored.Path);
            var displayName = stored.Name ?? DefaultClientName(stored.Path);
            return new ClientEntry(stored.Id, stored.Path, version, null, displayName, stored.Origin);
        }
        catch (ClientVersionException ex)
        {
            var displayName = stored.Name ?? DefaultClientName(stored.Path);
            return new ClientEntry(stored.Id, stored.Path, null, ServiceErrors.FromClientVersion(ex), displayName, stored.Origin);
        }
        catch (StoreException ex)
        {
            var displayName = stored.Name ?? DefaultClientName(stored.Path);
            return new ClientEntry(stored.Id, stored.Path, null, ServiceErrors.FromException(ex), displayName, stored.Origin);
        }
    }

    private void TryRecoverPendingStateUndo()
    {
        try
        {
            var journal = _journal.Load();
            if (journal is not { FilesUndone: true })
                return;

            if (!ClientFilesMatchFileUndoSnapshot(journal))
                return;

            // Prefer global recovery when state.Undo is pending. When Clear promoted another client into the
            // top-level journal after a scoped undo cleared state.Undo, recover that client's pending
            // UndoByClient entry without treating the promotion as global ownership.
            if (_active.Status().CanUndo)
            {
                _active.Undo(null, journal.ClientId);
            }
            else if (journal.ClientId is not null && _active.HasPendingUndo(journal.ClientId))
            {
                _active.Undo(journal.ClientId, journal.ClientId);
            }
            else
            {
                return;
            }

            if (journal.BackupId is not null && journal.ClientId is not null)
                CreateApplier().MarkBackupUndone(journal.ClientId, journal.BackupId);
            _journal.Clear(journal.ClientId);
        }
        catch
        {
            // Leave the journal for the user; never re-run file undo on startup.
        }
    }

    private bool ComputeCanUndo()
    {
        try
        {
            if (!_active.Status().CanUndo)
                return false;
            var journal = _journal.Load();
            if (journal is null)
                return true;
            if (journal.BackupId is null)
                return true;
            return journal.ClientId is not null && BackupIsPending(journal.ClientId, journal.BackupId);
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<string> BuildUndoClientFilesNotice(
        Server? restoredActive,
        IReadOnlyList<UndoFileResult> fileResults,
        StoredClient? switchedClient)
    {
        var lines = new List<string>();
        // State-only undo writes nothing. Always say so, including undo back to no active server and when every client already matches.
        if (fileResults.Count == 0)
            lines.Add("Client files: not changed.");
        else if (switchedClient is not null)
            lines.Add($"Client files: restored '{switchedClient.Path}' to its state before the switch.");
        else
            lines.Add("Client files: restored to their state before the switch.");

        // No active server means there is nothing for registered clients to drift from.
        if (restoredActive is null)
            return lines;

        foreach (var entry in ListClientsNotMatchingActive(restoredActive))
            lines.Add(ClientSyncMessages.DriftLine(restoredActive, entry.Path));

        return lines;
    }

    private bool ClientFilesMatchFileUndoSnapshot(LastSwitchEntry journal)
    {
        if (journal.ClientId is null || journal.BackupId is null)
            return false;

        var client = _clients.FindById(journal.ClientId);
        if (client is null)
            return false;

        var manifestPath = Path.Combine(_activeBackupRoot, journal.ClientId, journal.BackupId, "manifest.json");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            ClientPathSecurity.EnsureTrustedDirectoryPath(client.Path);
            var manifest = BackupManifest.FromJson(PrivateStorage.ReadBounded(manifestPath, PrivateStorage.MaxJsonBytes), manifestPath);
            foreach (var file in manifest.Files)
            {
                var relative = file.Path.Replace('\\', '/');
                var path = ClientPaths.ResolveManagedPath(ClientPaths.CanonicalRoot(client.Path), relative);
                if (!file.Existed)
                {
                    if (File.Exists(path))
                        return false;
                    continue;
                }

                if (!File.Exists(path) || file.Sha256Before is null)
                    return false;

                var hash = ClientFilesIo.Sha256Hex(File.ReadAllBytes(path));
                if (!string.Equals(hash, file.Sha256Before, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private List<ClientEntry> ListClientsNotMatchingActive(Server active)
    {
        var applier = CreateApplier();
        var drift = new List<ClientEntry>();
        foreach (var stored in _clients.List())
        {
            try
            {
                var version = ReadStoredClientVersion(stored.Path);
                var plan = applier.Plan(BuildRequest(stored.Id, active, stored.Path, version));
                if (!plan.IsEmpty)
                    drift.Add(ToClientEntry(stored));
            }
            catch (Exception ex) when (ex is ClientVersionException or StoreException)
            {
                drift.Add(ToClientEntry(stored));
            }
        }

        return drift;
    }

    private bool BackupIsPending(string installationId, string backupId)
    {
        var manifestPath = Path.Combine(_activeBackupRoot, installationId, backupId, "manifest.json");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            var bytes = PrivateStorage.ReadBounded(manifestPath, PrivateStorage.MaxJsonBytes);
            var manifest = BackupManifest.FromJson(bytes, manifestPath);
            return manifest.UndoneAt is null;
        }
        catch
        {
            return false;
        }
    }

    private BackupEntry EnrichBackup(BackupEntry backup, string clientPath, IReadOnlyDictionary<string, string> servers)
    {
        var pending = _active.HasPendingUndo(backup.ClientId)
                      && _journal.Load(backup.ClientId)?.BackupId == backup.BackupId;
        return backup with
        {
            ClientPath = clientPath,
            ServerName = servers.GetValueOrDefault(backup.ServerId),
            IsPendingUndo = pending,
        };
    }

    private static ServiceResult<Unit> RollbackSwitchApply(
        IClientFilesApplier applier,
        string installationId,
        string clientRoot,
        string backupId,
        InstallationLock? installationLock,
        string? manifestSha256)
    {
        try
        {
            var undo = applier.Undo(installationId, clientRoot, backupId, markUndone: true, installationLock, manifestSha256);
            if (undo.HasConflicts || !string.Equals(undo.BackupId, backupId, StringComparison.Ordinal))
                return ServiceResult<Unit>.Fail(ServiceErrors.Inconsistent(new ServiceError(ServiceErrorCode.InconsistentState, Consistency: Consistency.Inconsistent, Diagnostic: "Rollback reported conflicts or the wrong backup id.")));

            return ServiceResult<Unit>.Ok(default);
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.Inconsistent(ServiceErrors.FromException(ex, Consistency.Inconsistent)));
        }
    }

    /// <summary>Per-server override wins when set; otherwise the global Settings toggle (off by default).</summary>
    private bool ShouldClearCacheOnSwitch(Server server)
    {
        if (server.ClearCacheOnSwitch is { } overrideValue)
            return overrideValue;
        try
        {
            return _settings.GetStorageSettings().ClearCacheOnSwitch;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ServerEntry ToServerEntry(Server server, string? activeId, string? selectedClientId, ClientVersion? clientVersion)
    {
        string? clientId = server.ClientId;
        if (clientId is not null && _clients.FindById(clientId) is null)
            clientId = null;
        var compatible = selectedClientId is null || clientVersion is null
            ? true
            : CompatibilityFilter.IsCompatible(server, clientVersion);
        return new ServerEntry(
            server.Id,
            server.Name,
            server.Host,
            server.Realm,
            server.Realmlist,
            server.Account,
            server.RequiredClientBuild,
            clientId,
            server.Id == activeId,
            compatible,
            server.ClearCacheOnSwitch);
    }

}
