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
            _active.Switch(server);
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
            var journal = _journal.Load();
            if (journal is null || !_active.Status().CanUndo)
                return ServiceResult<UndoOutcome>.Fail(new ServiceError(ServiceErrorCode.NothingToUndo));

            var client = _clients.FindById(journal.ClientId)
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
            if (journal.BackupId is not null)
            {
                try
                {
                    var undoFiles = applier.Undo(journal.ClientId);
                    fileResults = undoFiles.Files;
                    if (undoFiles.HasConflicts)
                    {
                        var conflicts = undoFiles.Files
                            .Where(f => f.Status == UndoFileStatus.Conflict)
                            .Select(f => f.RelativePath.Replace('\\', '/'))
                            .ToList();
                        return ServiceResult<UndoOutcome>.Fail(ServiceErrors.UndoConflict(conflicts));
                    }
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

            _journal.Clear();
            var entry = restored is null ? null : ToServerEntry(restored, restored.Id, journal.ClientId, null);
            return ServiceResult<UndoOutcome>.Ok(new UndoOutcome(entry, fileResults));
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
            _journal.Clear();
            _active.DiscardPendingUndo();
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
            var canUndo = _journal.Load() is not null;
            return ServiceResult<PortalStatus>.Ok(new PortalStatus(active, canUndo, status.ProjectionInSync, _activeBackupRoot));
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
            progress?.Report(new ServiceProgress(ServiceStep.WriteFiles, StepState.Started));
            try
            {
                if (!plan.IsEmpty)
                {
                    var applied = applier.Apply(plan);
                    backupId = applied.BackupId;
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

            if (!RunStep(progress, ServiceStep.Activate, () => _active.Switch(server), out var activateError))
            {
                if (backupId is not null)
                {
                    try
                    {
                        applier.Undo(request.ClientId);
                    }
                    catch (Exception rollbackEx)
                    {
                        return ServiceResult<SwitchOutcome>.Fail(ServiceErrors.Inconsistent(ServiceErrors.FromException(rollbackEx, Consistency.Inconsistent)));
                    }
                }

                return ServiceResult<SwitchOutcome>.Fail(activateError! with { Consistency = Consistency.RolledBack });
            }

            if (!sameServerActive)
                _journal.Save(new LastSwitchEntry(request.ClientId, backupId));
            else if (!plan.IsEmpty)
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
