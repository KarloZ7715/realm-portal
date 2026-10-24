using Avalonia.Headless.XUnit;
using RealmPortal.App.Controls;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>Step 13: server edit/delete UX and the multi-client selector with per-client concurrency.</summary>
public sealed class Step13ServerUiTests
{
    private sealed class FakeLaunch : IGameLaunchService
    {
        public int Launches { get; private set; }

        public void Launch(GameLaunchSpec spec) => Launches++;
    }

    private sealed class Probe : IGameProcessProbe
    {
        public HashSet<string> RunningRoots { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsGameRunning(string clientRoot) => RunningRoots.Contains(clientRoot);
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Backups);
            var getVariable = (string name) => name == ClientFilesApplier.BackupDirectoryVariable ? Backups : null;
            var service = new PortalService(Config, Projection, new PeClientVersionReader(), Launch, Probe, getVariable);
            Session = new PortalSession(service, new UiPreferencesStore(Config), getVariable);
            Loc.Instance.SetLanguage(Language.En);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Backups { get; }
        public Probe Probe { get; } = new();
        public FakeLaunch Launch { get; } = new();
        public PortalSession Session { get; }
        public MainViewModel Main { get; private set; } = null!;

        /// <summary>Creates a client folder and returns its path (with a WoW.exe of <paramref name="version"/>).</summary>
        public string CreateClient(string name, Version version)
        {
            var folder = _temp.Combine(name);
            Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(version));
            return folder;
        }

        public string AddClient(string folder, string displayName) =>
            Session.Service.AddClient(folder, displayName).Value!.Id;

        /// <summary>Starts the shell with the given clients marked "In use", in order.</summary>
        public void Start(params string[] inUseClientIds)
        {
            Session.UiPreferences.Save(new UiPreferences("en", inUseClientIds, FirstScanCompleted: true));
            Main = new MainViewModel(Session, autoTick: false);
            Main.Servers.ReloadFromService();
        }

        public ServerEntry AddServer(string name, string host, string? realm = null, int? requiredBuild = null, string? clientId = null) =>
            Session.Service.AddServer(new NewServer(name, host, realm, RequiredClientBuild: requiredBuild, ClientId: clientId)).Value!;

        public void Dispose() => _temp.Dispose();
    }

    private static readonly Version Build335 = new(3, 3, 5, 12340);
    private static readonly Version Build434 = new(4, 3, 4, 15595);

    [AvaloniaFact]
    public void CardShowsItsTargetClientWhenMoreThanOneClientIsSaved()
    {
        using var env = new Env();
        var a = env.AddClient(env.CreateClient("alpha", Build335), "Alpha");
        var b = env.AddClient(env.CreateClient("beta", Build434), "Beta");
        var server = env.AddServer("Pinned", "pinned.example.org", clientId: b);
        env.Start(a, b);

        Assert.True(env.Main.Servers.HasMultipleClients);
        var card = env.Main.Servers.Visible.Single(x => x.ServerId == server.Id);
        Assert.True(card.ShowClientLabel);
        Assert.Contains("Beta", card.ClientLabel);
    }

    [AvaloniaFact]
    public void ClientFilterHidesServersTheSelectedClientCannotPlay()
    {
        using var env = new Env();
        var a = env.AddClient(env.CreateClient("alpha", Build335), "Alpha");
        var b = env.AddClient(env.CreateClient("beta", Build434), "Beta");
        var onlyA = env.AddServer("OnlyA", "onlya.example.org", requiredBuild: 12340);
        env.Start(a, b);

        Assert.True(env.Main.Servers.ShowClientFilter);
        Assert.Contains(env.Main.Servers.Visible, x => x.ServerId == onlyA.Id);

        env.Main.Servers.SelectedClientFilter = env.Main.Servers.ClientFilterOptions.Single(o => o.ClientId == b);
        Assert.DoesNotContain(env.Main.Servers.Visible, x => x.ServerId == onlyA.Id);

        env.Main.Servers.SelectedClientFilter = env.Main.Servers.ClientFilterOptions.Single(o => o.ClientId == a);
        Assert.Contains(env.Main.Servers.Visible, x => x.ServerId == onlyA.Id);
    }

    [AvaloniaFact]
    public void RunningClientLocksOnlyItsOwnCards()
    {
        using var env = new Env();
        var alphaFolder = env.CreateClient("alpha", Build335);
        var betaFolder = env.CreateClient("beta", Build434);
        var a = env.AddClient(alphaFolder, "Alpha");
        var b = env.AddClient(betaFolder, "Beta");
        var onA = env.AddServer("OnA", "ona.example.org", clientId: a);
        var onB = env.AddServer("OnB", "onb.example.org", clientId: b);
        env.Start(a, b);

        env.Probe.RunningRoots.Add(alphaFolder);
        env.Main.Servers.ReloadFromService();

        var cardA = env.Main.Servers.All.Single(x => x.ServerId == onA.Id);
        var cardB = env.Main.Servers.All.Single(x => x.ServerId == onB.Id);
        Assert.False(cardA.CanAct);
        Assert.Equal(ServerCardLock.ClientRunning, cardA.Lock);
        Assert.Contains("Alpha", cardA.MenuDisabledHint);
        Assert.True(cardB.CanAct);
        Assert.Equal(ServerCardLock.None, cardB.Lock);
    }

    [AvaloniaFact]
    public void CardWithoutACompatibleInUseClientIsLockedWithTheMarkInUseReason()
    {
        using var env = new Env();
        var a = env.AddClient(env.CreateClient("alpha", Build335), "Alpha");
        var onlyA = env.AddServer("OnlyA", "onlya.example.org", requiredBuild: 15595);
        env.Start(a, a);

        var card = env.Main.Servers.All.Single(x => x.ServerId == onlyA.Id);
        Assert.Equal(ServerCardLock.NoInUseClient, card.Lock);
        Assert.False(card.CanPlay);
        Assert.Equal(Loc.Get("menu.disabled.noinuse"), card.MenuDisabledHint);
    }

    [AvaloniaFact]
    public async Task SecondClientPlaysWhileTheFirstOneKeepsRunning()
    {
        using var env = new Env();
        var alphaFolder = env.CreateClient("alpha", Build335);
        var betaFolder = env.CreateClient("beta", Build434);
        var a = env.AddClient(alphaFolder, "Alpha");
        var b = env.AddClient(betaFolder, "Beta");
        var onB = env.AddServer("OnB", "onb.example.org", clientId: b);
        env.Start(a, b);

        env.Probe.RunningRoots.Add(alphaFolder);
        env.Main.Servers.ReloadFromService();
        Assert.Equal(1, env.Main.Servers.RunningCount);

        var card = env.Main.Servers.Visible.Single(x => x.ServerId == onB.Id);
        await env.Main.Servers.PlayAsync(card);

        Assert.Equal(ServersPhase.Running, env.Main.Servers.Phase);
        Assert.Equal(2, env.Main.Servers.RunningCount);
        Assert.Contains("2", env.Main.Servers.BannerBody);
        Assert.Equal(BannerSeverity.Info, env.Main.Servers.BannerSeverity);
    }

    [AvaloniaFact]
    public void EditFormPrefillsFromTheEntryAndUpdatesTheServer()
    {
        using var env = new Env();
        var a = env.AddClient(env.CreateClient("alpha", Build335), "Alpha");
        var server = env.AddServer("Original", "orig.example.org", realm: "Example Realm", clientId: a);
        env.Start(a);

        var entry = env.Session.Service.ListServers(compatibleOnly: false).Value!.Single(s => s.Id == server.Id);
        var form = new AddServerViewModel(env.Main.Clients, env.Session, entry, () => { }, () => { });
        Assert.True(form.IsEditing);
        Assert.Equal(Loc.Get("edit.server.title"), form.FormTitle);
        Assert.Equal(Loc.Get("edit.server.save"), form.SaveLabel);
        Assert.Equal("Original", form.Name);
        Assert.Equal("orig.example.org", form.Address);
        Assert.Equal("Example Realm", form.Realm);

        form.Name = "Renamed";
        form.Realm = "Stormscale";
        form.SaveCommand.Execute(null);

        var updated = env.Session.Service.ListServers(compatibleOnly: false).Value!.Single(s => s.Id == server.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("Stormscale", updated.Realm);
        Assert.False(form.HasSaveError);
    }

    [AvaloniaFact]
    public void AddFormRejectsADuplicateAddressAndRealm()
    {
        using var env = new Env();
        var a = env.AddClient(env.CreateClient("alpha", Build335), "Alpha");
        env.AddServer("Existing", "dup.example.org", realm: "Example Realm", clientId: a);
        env.Start(a);

        var form = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        form.Name = "Copy";
        form.Address = "dup.example.org";
        form.Realm = "Example Realm";
        form.Submitted = true;
        form.SaveCommand.Execute(null);

        Assert.True(form.HasSaveError);
        Assert.Equal(Loc.Get("add.error.duplicate"), form.SaveError);
        Assert.Single(env.Session.Service.ListServers(compatibleOnly: false).Value!);
    }

    [AvaloniaFact]
    public async Task DeleteRemovesTheServerOnlyAfterConfirmation()
    {
        using var env = new Env();
        var a = env.AddClient(env.CreateClient("alpha", Build335), "Alpha");
        var server = env.AddServer("Doomed", "doomed.example.org", clientId: a);
        env.Start(a);

        var card = env.Main.Servers.Visible.Single(x => x.ServerId == server.Id);
        env.Main.Servers.ConfirmDeleteServerAsync = _ => Task.FromResult(false);
        await env.Main.Servers.DeleteServerAsync(card);
        Assert.Single(env.Session.Service.ListServers(compatibleOnly: false).Value!);

        env.Main.Servers.ConfirmDeleteServerAsync = _ => Task.FromResult(true);
        await env.Main.Servers.DeleteServerAsync(card);
        Assert.Empty(env.Session.Service.ListServers(compatibleOnly: false).Value!);
        Assert.DoesNotContain(env.Main.Servers.Visible, x => x.ServerId == server.Id);
    }
}
