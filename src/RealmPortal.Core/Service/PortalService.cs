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
    private readonly IClientVersionReader _versionReader;
    private readonly IGameLaunchService _launchService;
    private readonly IGameProcessProbe _probe;
    private readonly Func<string, string?> _getVariable;
    private readonly IAtomicFileWriter _writer;
    private readonly ServerStore _servers;
    private readonly ClientStore _clients;
    private readonly ActiveServerManager _active;
    private readonly LastSwitchJournal _journal;
    private readonly PortalSettingsStore _settings;
    private readonly string _activeBackupRoot;

    public PortalService(
        string configDirectory,
        string projectionDirectory,
        IClientVersionReader versionReader,
        IGameLaunchService launchService,
        IGameProcessProbe? probe = null,
        Func<string, string?>? getVariable = null,
        IAtomicFileWriter? writer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionDirectory);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(launchService);
        _configDirectory = configDirectory;
        _projectionDirectory = projectionDirectory;
        _versionReader = versionReader;
        _launchService = launchService;
        _probe = probe ?? new GameProcessProbe();
        _getVariable = getVariable ?? Environment.GetEnvironmentVariable;
        _writer = writer ?? new AtomicFileWriter();
        _servers = new ServerStore(configDirectory, _writer);
        _clients = new ClientStore(configDirectory, _writer);
        _active = new ActiveServerManager(_servers, configDirectory, projectionDirectory, _writer);
        _journal = new LastSwitchJournal(configDirectory, _writer);
        _settings = new PortalSettingsStore(configDirectory, _writer, _getVariable);
        _activeBackupRoot = _settings.ActiveBackupFolder;
        TryRecoverPendingStateUndo();
    }

    public ServiceResult<IReadOnlyList<ClientEntry>> ListClients()
    {
        try
        {
            return ServiceResult<IReadOnlyList<ClientEntry>>.Ok(_clients.List().Select(ToClientEntry).ToList());
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<ClientEntry>>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<ClientEntry> AddClient(string folder)
    {
        if (folder is null)
            throw new ArgumentNullException(nameof(folder));

        try
        {
            var version = _versionReader.Read(folder);
            var stored = _clients.Add(folder);
            return ServiceResult<ClientEntry>.Ok(new ClientEntry(stored.Id, stored.Path, version, null));
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
            if (!_clients.Remove(clientId))
                return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.ClientNotFound));
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
                    clientVersion = _versionReader.Read(client.Path);
                }
                catch (ClientVersionException)
                {
                    clientVersion = null;
                }
            }

            var activeId = _active.GetActive()?.Id;
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
                clientId: server.ClientId);
            _servers.Add(created);
            var activeId = _active.GetActive()?.Id;
            return ServiceResult<ServerEntry>.Ok(ToServerEntry(created, activeId, server.ClientId, null));
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
            if (!_active.Remove(serverId))
                return ServiceResult<Unit>.Fail(new ServiceError(ServiceErrorCode.ServerNotFound));
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
            var server = _servers.List().FirstOrDefault(s => s.Id == serverId)
                         ?? throw new StoreException($"No server matches '{serverId}'.");
            var before = _active.GetActive();
            _active.Switch(server);
            if (before?.Id != server.Id)
                _journal.Save(new LastSwitchEntry(null, null));
            return ServiceResult<ServerEntry>.Ok(ToServerEntry(server, server.Id, null, null));
        }
        catch (Exception ex)
        {
            return ServiceResult<ServerEntry>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<UndoOutcome> Undo()
    {
        try
        {
            if (!_active.Status().CanUndo)
                return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.NothingToUndo));

            var journal = _journal.Load() ?? new LastSwitchEntry(null, null);

            StoredClient? client = null;
            if (journal.BackupId is not null)
            {
                if (journal.ClientId is null)
                    return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.NothingToUndo));

                client = _clients.FindById(journal.ClientId)
                         ?? throw new StoreException("The client of the last switch is no longer registered.");

                try
                {
                    if (_probe.IsGameRunning(client.Path))
                        return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.GameRunning, Path: client.Path));
                }
                catch (Exception ex)
                {
                    return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.ProcessCheckFailed, Path: client.Path, Diagnostic: ex.Message));
                }
            }

            if (_active.PendingUndoServerId is { } missingId
                && _servers.List().All(s => s.Id != missingId))
            {
                return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.UndoTargetMissing));
            }

            var undoTarget = _active.GetUndoTarget();
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

            var applier = CreateApplier();
            IReadOnlyList<UndoFileResult> fileResults = [];
            if (!journal.FilesUndone && journal.BackupId is not null && client is not null)
            {
                try
                {
                    var undoFiles = applier.Undo(journal.ClientId!, client.Path, journal.BackupId, markUndone: false);
                    fileResults = undoFiles.Files;
                    if (undoFiles.HasConflicts || !string.Equals(undoFiles.BackupId, journal.BackupId, StringComparison.Ordinal))
                    {
                        var conflicts = undoFiles.Files
                            .Where(f => f.Status == UndoFileStatus.Conflict)
                            .Select(f => f.RelativePath.Replace('\\', '/'))
                            .ToList();
                        return ServiceResult<UndoOutcome>.Fail(ServiceErrors.UndoConflict(conflicts));
                    }

                    _journal.MarkFilesUndone();
                }
                catch (ClientFilesException ex)
                {
                    return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromClientFiles(ex));
                }
            }

            Server? restored;
            try
            {
                restored = _active.Undo();
            }
            catch (Exception ex)
            {
                try
                {
                    if (client is null || journal.ClientId is null)
                        throw new InvalidOperationException("No client files were undone before the failed state undo.");

                    var active = _active.GetActive()
                                 ?? throw new InvalidOperationException("No active server to re-apply after a failed undo.");
                    var version = _versionReader.Read(client.Path);
                    var plan = applier.Plan(BuildRequest(journal.ClientId, active, client.Path, version));
                    if (!plan.IsEmpty)
                        applier.Apply(plan);
                }
                catch (Exception rollbackEx)
                {
                    return ServiceResult<UndoOutcome>.Fail(ServiceErrors.Inconsistent(ServiceErrors.FromException(rollbackEx, Consistency.Inconsistent)));
                }

                return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromException(ex, Consistency.RolledBack));
            }

            if (journal.BackupId is not null && journal.ClientId is not null)
                applier.MarkBackupUndone(journal.ClientId, journal.BackupId);

            _journal.Clear();
            var entry = restored is null ? null : ToServerEntry(restored, restored.Id, journal.ClientId, null);
            var notice = BuildUndoClientFilesNotice(restored, journal, fileResults, client);
            return ServiceResult<UndoOutcome>.Ok(new UndoOutcome(entry, fileResults, notice));
        }
        catch (Exception ex)
        {
            return ServiceResult<UndoOutcome>.Fail(ServiceErrors.FromException(ex));
        }
    }

    public ServiceResult<PortalStatus> DiscardUndo()
    {
        try
        {
            _active.DiscardPendingUndo();
            _journal.Clear();
            return GetStatus();
        }
        catch (Exception ex)
        {
            return ServiceResult<PortalStatus>.Fail(ServiceErrors.FromException(ex));
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
                new PortalStatus(active, canUndo, status.ProjectionInSync, _activeBackupRoot, drifting));
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
            return ServiceResult<Unit>.Ok(default);
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.FromException(ex));
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
                version = _versionReader.Read(client.Path);
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

            var applier = CreateApplier();
            var active = _active.GetActive();
            var sameServerActive = active?.Id == server.Id;
            var previousEntry = active is null ? null : ToServerEntry(active, active.Id, request.ClientId, version);

            ClientFilesPlan plan = null!;
            if (!RunStep(progress, ServiceStep.Plan, () => plan = applier.Plan(BuildRequest(request.ClientId, server, client.Path, version)), out var planError))
                return ServiceResult<SwitchOutcome>.Fail(planError!);

            string? backupId = null;
            IReadOnlyList<string> changedFiles = [];
            InstallationLock? installationLock = null;
            progress?.Report(new ServiceProgress(ServiceStep.WriteFiles, StepState.Started));
            try
            {
                if (!plan.IsEmpty)
                {
                    installationLock = applier.AcquireInstallationLock(request.ClientId);
                    var applied = applier.Apply(plan, installationLock);
                    backupId = applied.BackupId;
                    changedFiles = applied.Edits.Select(e => e.RelativePath.Replace('\\', '/')).ToList();
                }

                progress?.Report(new ServiceProgress(ServiceStep.WriteFiles, StepState.Completed));
            }
            catch (Exception ex)
            {
                installationLock?.Dispose();
                var error = ServiceErrors.FromException(ex, Consistency.Unchanged);
                progress?.Report(new ServiceProgress(ServiceStep.WriteFiles, StepState.Failed, error));
                return ServiceResult<SwitchOutcome>.Fail(error);
            }

            try
            {
                if (!RunStep(progress, ServiceStep.Activate, () => _active.Switch(server), out var activateError))
                {
                    if (backupId is not null)
                    {
                        var rollback = RollbackSwitchApply(applier, request.ClientId, client.Path, backupId, installationLock);
                        if (!rollback.Succeeded)
                        {
                            installationLock?.Dispose();
                            return ServiceResult<SwitchOutcome>.Fail(rollback.Error!);
                        }
                    }

                    installationLock?.Dispose();
                    return ServiceResult<SwitchOutcome>.Fail(activateError! with
                    {
                        Consistency = backupId is not null ? Consistency.RolledBack : Consistency.Unchanged,
                    });
                }
            }
            catch
            {
                installationLock?.Dispose();
                throw;
            }

            installationLock?.Dispose();

            if (!sameServerActive)
                _journal.Save(new LastSwitchEntry(request.ClientId, backupId));

            var activeEntry = ToServerEntry(server, server.Id, request.ClientId, version);
            var previous = sameServerActive ? activeEntry : previousEntry;

            if (launch)
            {
                if (!RunStep(progress, ServiceStep.Launch, () => LaunchGame(client.Path, _settings.GetLauncherSettings()), out var launchError))
                    return ServiceResult<SwitchOutcome>.Fail(launchError! with { Code = ServiceErrorCode.LaunchFailed });
            }

            return ServiceResult<SwitchOutcome>.Ok(new SwitchOutcome(activeEntry, previous, backupId, changedFiles));
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

    private ClientEntry ToClientEntry(StoredClient stored)
    {
        try
        {
            var version = _versionReader.Read(stored.Path);
            return new ClientEntry(stored.Id, stored.Path, version, null);
        }
        catch (ClientVersionException ex)
        {
            return new ClientEntry(stored.Id, stored.Path, null, ServiceErrors.FromClientVersion(ex));
        }
    }

    private void TryRecoverPendingStateUndo()
    {
        try
        {
            var journal = _journal.Load();
            if (journal is not { FilesUndone: true } || !_active.Status().CanUndo)
                return;

            if (!ClientFilesMatchFileUndoSnapshot(journal))
                return;

            _active.Undo();
            if (journal.BackupId is not null && journal.ClientId is not null)
                CreateApplier().MarkBackupUndone(journal.ClientId, journal.BackupId);
            _journal.Clear();
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

    private string? BuildUndoClientFilesNotice(
        Server? restoredActive,
        LastSwitchEntry journal,
        IReadOnlyList<UndoFileResult> fileResults,
        StoredClient? switchedClient)
    {
        if (restoredActive is null)
            return null;

        var drift = ListClientsNotMatchingActive(restoredActive);
        var lines = new List<string>();

        if (fileResults.Count > 0 && switchedClient is not null)
            lines.Add($"Client files: restored '{switchedClient.Path}' to its state before the switch.");
        else if (journal.BackupId is null && drift.Count > 0)
            lines.Add("Client files: not changed.");

        foreach (var entry in drift)
            lines.Add(ClientSyncMessages.DriftLine(restoredActive, entry.Path));

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
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
            var manifest = BackupManifest.FromJson(File.ReadAllBytes(manifestPath), manifestPath);
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
                var version = _versionReader.Read(stored.Path);
                var plan = applier.Plan(BuildRequest(stored.Id, active, stored.Path, version));
                if (!plan.IsEmpty)
                    drift.Add(ToClientEntry(stored));
            }
            catch (ClientVersionException)
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
            var bytes = File.ReadAllBytes(manifestPath);
            var manifest = BackupManifest.FromJson(bytes, manifestPath);
            return manifest.UndoneAt is null;
        }
        catch
        {
            return false;
        }
    }

    private static ServiceResult<Unit> RollbackSwitchApply(
        IClientFilesApplier applier,
        string installationId,
        string clientRoot,
        string backupId,
        InstallationLock? installationLock)
    {
        try
        {
            var undo = applier.Undo(installationId, clientRoot, backupId, markUndone: true, installationLock);
            if (undo.HasConflicts || !string.Equals(undo.BackupId, backupId, StringComparison.Ordinal))
                return ServiceResult<Unit>.Fail(ServiceErrors.Inconsistent(new ServiceError(ServiceErrorCode.InconsistentState, Consistency: Consistency.Inconsistent, Diagnostic: "Rollback reported conflicts or the wrong backup id.")));

            return ServiceResult<Unit>.Ok(default);
        }
        catch (Exception ex)
        {
            return ServiceResult<Unit>.Fail(ServiceErrors.Inconsistent(ServiceErrors.FromException(ex, Consistency.Inconsistent)));
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
            compatible);
    }

}
