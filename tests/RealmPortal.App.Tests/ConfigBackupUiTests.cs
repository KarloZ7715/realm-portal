using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;
using TempDirectory = RealmPortal.Tests.Support.TempDirectory;

namespace RealmPortal.App.Tests;

/// <summary>Settings card for app config .bak restore (#196 UI).</summary>
public sealed class ConfigBackupUiTests
{
    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            var projection = _temp.Combine("projection");
            var backups = _temp.Combine("backups");
            var home = _temp.Combine("home");
            foreach (var d in new[] { Config, projection, backups, home })
                Directory.CreateDirectory(d);
            Session = TestSessions.Create(Config, projection, backups, home, new NoLaunch(), new NeverRunningGameProbe(), seedPreferences: false);
            Loc.Instance.SetLanguage(Language.En);
        }

        public string Config { get; }
        public PortalSession Session { get; }

        public string CreateClient(string name)
        {
            var folder = _temp.Combine(name);
            Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            return folder;
        }

        public void Dispose() => _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task Settings_ListsConfigFiles_AndRestoresDamagedServersFromBak()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo Realm", "logon.example.com", ClientId: cid));
        // Change servers so a .bak exists with the previous version, then damage the main file.
        env.Session.Service.AddServer(new NewServer("Second", "play.example.net", ClientId: cid));
        var serversPath = Path.Combine(env.Config, "servers.json");
        Assert.True(File.Exists(serversPath + ".bak"));
        var bak = File.ReadAllBytes(serversPath + ".bak");
        File.WriteAllText(serversPath, "{ not json");

        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();

        var serversRow = Assert.Single(main.Settings.ConfigBackups, r => r.Kind == ConfigFileKind.Servers);
        Assert.Equal(ConfigFileState.Damaged, serversRow.Current);
        Assert.Equal(ConfigFileState.Valid, serversRow.Backup);
        Assert.True(serversRow.CanRestore);

        var prefsRow = Assert.Single(main.Settings.ConfigBackups, r => r.IsPreferences);
        Assert.False(prefsRow.CanRestore);

        main.Settings.ConfirmRestoreConfigAsync = _ => Task.FromResult(true);
        await serversRow.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(bak, File.ReadAllBytes(serversPath));
        Assert.Contains("Restored", main.Settings.ConfigMessage);
        Assert.False(Assert.Single(main.Settings.ConfigBackups, r => r.Kind == ConfigFileKind.Servers).CanRestore);

        // List reloads from the restored store (one server from the .bak before Second was added).
        main.Servers.ReloadFromService();
        Assert.Single(main.Servers.Visible);
        Assert.Equal("Demo Realm", main.Servers.Visible[0].Name);
    }

    [AvaloniaFact]
    public void Restore_IsNotOfferedWhenCurrentIsValid()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        Assert.All(main.Settings.ConfigBackups, row => Assert.False(row.CanRestore));
    }

    [AvaloniaFact]
    public async Task Restore_CancelKeepsDamagedFile()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.Service.AddServer(new NewServer("Other", "play.example.net", ClientId: cid));
        var serversPath = Path.Combine(env.Config, "servers.json");
        File.WriteAllText(serversPath, "{ not json");
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        var row = Assert.Single(main.Settings.ConfigBackups, r => r.CanRestore);
        main.Settings.ConfirmRestoreConfigAsync = _ => Task.FromResult(false);
        await row.RestoreCommand.ExecuteAsync(null);
        Assert.Equal("{ not json", File.ReadAllText(serversPath));
        Assert.Null(main.Settings.ConfigMessage);
    }
}
