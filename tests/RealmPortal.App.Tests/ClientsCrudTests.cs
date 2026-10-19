using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

public sealed class ClientsCrudTests
{
    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not used by client screen tests.");
    }

    private sealed class Probe(bool running = false) : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => running;
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env(bool running = false, string[]? additionalRoots = null, bool firstScanCompleted = true)
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Backups);
            var service = new PortalService(
                Config,
                Projection,
                new PeClientVersionReader(),
                new NoLaunch(),
                new Probe(running),
                name => name == ClientFilesApplier.BackupDirectoryVariable ? Backups : null,
                scanOptions: new ClientScanOptions
                {
                    IncludeDefaultRoots = false,
                    IncludeSystemRoots = false,
                    AdditionalRoots = additionalRoots ?? [],
                });
            Session = new PortalSession(service, new UiPreferencesStore(Config),
                name => name == ClientFilesApplier.BackupDirectoryVariable ? Backups : null);
            Session.UiPreferences.Save(new UiPreferences("en", [], FirstScanCompleted: firstScanCompleted));
            Main = new MainViewModel(Session, autoTick: false);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Backups { get; }
        public PortalSession Session { get; }
        public MainViewModel Main { get; }

        public string CreateClient(string name)
        {
            var folder = _temp.Combine(name);
            Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            return folder;
        }

        public void Dispose() => _temp.Dispose();
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(predicate(), "The requested client screen state was not reached.");
    }

    [AvaloniaFact]
    public async Task FirstScanRunsOnceAndPersistsCompletionWithCounts()
    {
        using var temp = new TempDirectory();
        var scanRoot = temp.Combine("scan");
        Directory.CreateDirectory(scanRoot);
        var game = Path.Combine(scanRoot, "game");
        Directory.CreateDirectory(Path.Combine(game, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(game, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        using var env = new Env(additionalRoots: [scanRoot], firstScanCompleted: false);

        await WaitUntil(() => !env.Main.Clients.IsScanning);

        Assert.True(env.Session.UiPreferences.Load().FirstScanCompleted);
        Assert.Single(env.Main.Clients.Items);
        Assert.Contains("1 client(s) added", env.Main.Clients.Notice);
        Assert.Contains("0 server(s) imported", env.Main.Clients.Notice);
        Assert.Contains("0 already known", env.Main.Clients.Notice);
        Assert.Contains("0 client(s) need attention", env.Main.Clients.Notice);
    }

    [AvaloniaFact]
    public async Task RescanReportsKnownClientsAndRefreshesServerProjectionOnNoChange()
    {
        using var env = new Env();
        var path = env.CreateClient("known");
        Assert.True(env.Session.Service.AddClient(path).Succeeded);
        env.Main.Clients.ReloadFromService();
        var refreshes = 0;
        env.Main.Clients.ClientsChanged += () => refreshes++;

        await env.Main.Clients.RescanCommand.ExecuteAsync(null);

        Assert.Contains("no new clients or servers found", env.Main.Clients.Notice);
        Assert.Contains("1 already known", env.Main.Clients.Notice);
        Assert.Contains("0 client(s) need attention", env.Main.Clients.Notice);
        Assert.True(refreshes > 0);
        Assert.Single(env.Main.Clients.Items);
    }

    [AvaloniaFact]
    public async Task EditClientUpdatesNameAndPathAndMigratesInUsePreference()
    {
        using var env = new Env();
        var oldPath = env.CreateClient("old-client");
        var newPath = env.CreateClient("new-client");
        var added = env.Session.Service.AddClient(oldPath).Value!;
        env.Session.UiPreferences.Save(new UiPreferences("en", [added.Id], FirstScanCompleted: true));
        env.Main.Clients.ReloadFromService();
        var row = Assert.Single(env.Main.Clients.Items);

        row.EditCommand.Execute(null);
        var editor = Assert.IsType<EditClientViewModel>(env.Main.Content);
        editor.Name = "WotLK test";
        editor.Folder = newPath;
        await editor.SaveCommand.ExecuteAsync(null);

        var client = Assert.Single(env.Session.Service.ListClients().Value!);
        Assert.Equal("WotLK test", client.Name);
        Assert.Equal(Path.GetFullPath(newPath), client.Path);
        Assert.NotEqual(added.Id, client.Id);
        Assert.Contains(client.Id, env.Session.UiPreferences.Load().InUseSet);
        Assert.DoesNotContain(added.Id, env.Session.UiPreferences.Load().InUseSet);
        Assert.IsType<ClientsViewModel>(env.Main.Content);
    }

    [AvaloniaFact]
    public async Task RemoveClientRequiresConfirmationAndKeepsGameFilesAndBackups()
    {
        using var env = new Env();
        var path = env.CreateClient("remove-client");
        var client = env.Session.Service.AddClient(path).Value!;
        var clientBackup = Path.Combine(env.Backups, client.Id);
        Directory.CreateDirectory(clientBackup);
        File.WriteAllText(Path.Combine(clientBackup, "marker.txt"), "keep");
        env.Main.Clients.ReloadFromService();
        var row = Assert.Single(env.Main.Clients.Items);
        var confirmed = false;
        env.Main.Clients.ConfirmRemoveAsync = _ => Task.FromResult(confirmed);

        row.RemoveCommand.Execute(null);
        await Task.Delay(30);
        Assert.Single(env.Session.Service.ListClients().Value!);

        confirmed = true;
        row.RemoveCommand.Execute(null);
        await WaitUntil(() => env.Main.Clients.Items.Count == 0);

        Assert.Empty(env.Session.Service.ListClients().Value!);
        Assert.True(File.Exists(Path.Combine(path, "Wow.exe")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(clientBackup, "marker.txt")));
    }

    [AvaloniaFact]
    public void RunningOrUnknownClientCannotBeEditedOrRemoved()
    {
        using var env = new Env(running: true);
        var path = env.CreateClient("running");
        Assert.True(env.Session.Service.AddClient(path).Succeeded);
        env.Main.Clients.ReloadFromService();
        var row = Assert.Single(env.Main.Clients.Items);

        Assert.True(row.IsRunning);
        Assert.False(row.EditCommand.CanExecute(null));
        Assert.False(row.RemoveCommand.CanExecute(null));
    }
}
