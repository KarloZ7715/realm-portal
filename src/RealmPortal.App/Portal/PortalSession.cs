using RealmPortal.Core;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Updates;

namespace RealmPortal.App.Portal;

/// <summary>Wires <see cref="IPortalService"/> with injectable paths (env vars) and UI preferences.</summary>
public sealed class PortalSession
{
    public PortalSession(
        IPortalService service,
        UiPreferencesStore uiPreferences,
        Func<string, string?>? getVariable = null,
        string? configDirectory = null,
        NetworkSettingsStore? networkSettings = null,
        IUpdateChecker? updateChecker = null)
    {
        Service = service;
        UiPreferences = uiPreferences;
        GetVariable = getVariable ?? Environment.GetEnvironmentVariable;
        var configDir = configDirectory
            ?? (service as PortalService)?.ConfigDirectory
            ?? PortalPaths.ConfigDirectory(GetVariable);
        NetworkSettings = networkSettings ?? new NetworkSettingsStore(configDir);
        UpdateChecker = updateChecker ?? CreateDefaultUpdateChecker(NetworkSettings, configDir);
    }

    public IPortalService Service { get; }

    public UiPreferencesStore UiPreferences { get; }

    public Func<string, string?> GetVariable { get; }

    public NetworkSettingsStore NetworkSettings { get; }

    public IUpdateChecker UpdateChecker { get; }

    private static IUpdateChecker CreateDefaultUpdateChecker(NetworkSettingsStore network, string configDir)
    {
        var cache = new UpdateCacheStore(configDir);
        var feed = new GitHubReleaseFeed();
        var downloader = new UpdateDownloader();
        return new UpdateChecker(network, cache, feed, downloader);
    }

    public static PortalSession Create(Func<string, string?>? getVariable = null)
    {
        getVariable ??= Environment.GetEnvironmentVariable;
        var configDir = PortalPaths.ConfigDirectory(getVariable);
        var projectionDir = PortalPaths.ProjectionDirectory(getVariable);
        var service = new PortalService(
            configDir,
            projectionDir,
            new PeClientVersionReader(),
            new ProcessGameLauncher(),
            getVariable: getVariable);
        return new PortalSession(service, new UiPreferencesStore(configDir), getVariable, configDir);
    }

    public static PortalSession CreateForPaths(
        string configDir,
        string projectionDir,
        string backupDir,
        IGameLaunchService? launch = null,
        IGameProcessProbe? probe = null,
        ClientScanOptions? scanOptions = null)
    {
        launch ??= new ProcessGameLauncher();
        var getVariable = (string name) =>
            name == ClientFilesApplier.BackupDirectoryVariable ? backupDir : null;
        var service = new PortalService(
            configDir,
            projectionDir,
            new PeClientVersionReader(),
            launch,
            probe,
            getVariable: getVariable,
            scanOptions: scanOptions);
        return new PortalSession(service, new UiPreferencesStore(configDir), getVariable, configDir);
    }

    /// <summary>The client used for server list compatibility and switch/play when none is pinned on the server.</summary>
    public string? ResolveClientId(string? preferredServerClientId = null)
    {
        var list = Service.ListClients();
        var clients = list.Succeeded ? list.Value! : [];
        return ResolveClientId(clients, preferredServerClientId);
    }

    /// <summary>
    /// Same choice as <see cref="ResolveClientId(string?)"/> against an already loaded client list,
    /// so a screen can resolve every card without calling <c>ListClients</c> again.
    /// </summary>
    public string? ResolveClientId(IReadOnlyList<ClientEntry> clients, string? preferredServerClientId)
    {
        ArgumentNullException.ThrowIfNull(clients);
        if (preferredServerClientId is not null && clients.Any(c => c.Id == preferredServerClientId))
        {
            return preferredServerClientId;
        }

        if (clients.Count == 0)
        {
            return null;
        }

        var prefs = UiPreferences.Load();
        foreach (var id in prefs.InUseSet)
        {
            if (clients.Any(c => c.Id == id))
            {
                return id;
            }
        }

        return clients[0].Id;
    }

    public PortalStatus? StatusOrNull()
    {
        var status = Service.GetStatus();
        return status.Succeeded ? status.Value : null;
    }
}
