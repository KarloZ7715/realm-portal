using System.Text;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

public class PortalViewModelTests
{
    private sealed class FakeLaunch : IGameLaunchService
    {
        public int Launches { get; private set; }

        public void Launch(GameLaunchSpec spec) => Launches++;
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Projection);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Directory.CreateDirectory(Backups);
            Launch = new FakeLaunch();
            Session = PortalSession.CreateForPaths(Config, Projection, Backups, Launch);
            var clientId = Session.Service.AddClient(Client).Value!.Id;
            Session.UiPreferences.Save(new UiPreferences("en", [clientId]));
            Loc.Instance.SetLanguage(Language.En);
            Main = new MainViewModel(Session, () => Task.FromResult<string?>(Client), autoTick: false);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Client { get; }
        public string Backups { get; }
        public FakeLaunch Launch { get; }
        public PortalSession Session { get; }
        public MainViewModel Main { get; }

        public ServerEntry AddServer(string name, string host) =>
            Session.Service.AddServer(new NewServer(name, host)).Value!;

        public void PutRealmlist(string content) =>
            File.WriteAllBytes(Path.Combine(Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1.GetBytes(content));

        public void Dispose() => _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task Switch_ShowsUndo_AndUndoRestores()
    {
        using var env = new Env();
        var a = env.AddServer("A", "a.example.org");
        var b = env.AddServer("B", "b.example.org");
        env.Main.Servers.ReloadFromService();
        var first = env.Main.Servers.All.First(s => s.ServerId == a.Id);
        var second = env.Main.Servers.All.First(s => s.ServerId == b.Id);

        await env.Main.Servers.SwitchOnlyAsync(first);
        Assert.Equal(ServersPhase.Idle, env.Main.Servers.Phase);
        Assert.Equal(a.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        await env.Main.Servers.SwitchOnlyAsync(second);
        Assert.Equal(ServersPhase.Idle, env.Main.Servers.Phase);
        Assert.Equal(b.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.True(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.True(env.Main.Servers.ToastVisible);
        await ((IAsyncRelayCommand)env.Main.Servers.UndoCommand).ExecuteAsync(null);
        Assert.Equal(a.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.False(env.Main.Servers.ToastVisible);
    }

    [AvaloniaFact]
    public async Task Repair_WhenProjectionOutOfSync()
    {
        using var env = new Env();
        var b = env.AddServer("B", "b.example.org");
        env.Session.Service.SwitchServer(new SwitchRequest(env.Session.ResolveClientId()!, b.Id));
        File.Delete(Path.Combine(env.Projection, "activo.json"));
        env.Main.Servers.ReloadFromService();
        var card = env.Main.Servers.All.First(s => s.ServerId == b.Id);
        Assert.True(card.NeedsRepair);
        await env.Main.Servers.SwitchOnlyAsync(card);
        Assert.True(env.Session.StatusOrNull()!.ProjectionInSync);
    }

    [AvaloniaFact]
    public async Task ManualAdd_AppearsInList()
    {
        using var env = new Env();
        env.Main.Servers.ReloadFromService();
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => env.Main.Servers.ReloadFromService(), () => { });
        add.Name = "Hand";
        add.Address = "hand.example.org";
        add.SelectedClient = env.Main.Clients.Items[0];
        add.SaveCommand.Execute(null);
        env.Main.Servers.ReloadFromService();
        Assert.Contains(env.Main.Servers.All, s => s.Name == "Hand");
    }

    [AvaloniaFact]
    public async Task Play_InvokesLaunch()
    {
        using var env = new Env();
        var s = env.AddServer("Play", "play.example.org");
        env.Main.Servers.ReloadFromService();
        var card = env.Main.Servers.All.First(x => x.ServerId == s.Id);
        await env.Main.Servers.PlayAsync(card);
        Assert.Equal(1, env.Launch.Launches);
    }

    [AvaloniaFact]
    public void Settings_PersistsBackupFolder()
    {
        using var env = new Env();
        var settings = (SettingsViewModel)env.Main.Settings;
        var other = Path.Combine(env.Backups, "other");
        Directory.CreateDirectory(other);
        settings.Template = "wine {exe}";
        settings.BackupFolder = other;
        settings.SaveCommand.Execute(null);
        Assert.Equal(settings.BackupFolder, env.Session.Service.GetStorageSettings().Value!.BackupFolder);
    }

    [AvaloniaFact]
    public async Task DiscardUndo_AfterConflict()
    {
        using var env = new Env();
        var a = env.AddServer("A", "a.example.org");
        var b = env.AddServer("B", "b.example.org");
        env.PutRealmlist("set realmlist seed.example.org\r\n");
        env.Session.Service.SwitchServer(new SwitchRequest(env.Session.ResolveClientId()!, b.Id));
        env.PutRealmlist("set realmlist tamper.example.org\r\n");
        env.Main.Servers.ReloadFromService();
        env.Main.Servers.UndoCommand.Execute(null);
        await Task.Delay(200);
        Assert.True(env.Main.Servers.ShowDiscardUndo);
        env.Main.Servers.DiscardUndoCommand.Execute(null);
        await Task.Delay(200);
        Assert.False(env.Session.Service.GetStatus().Value!.CanUndo);
    }
}
