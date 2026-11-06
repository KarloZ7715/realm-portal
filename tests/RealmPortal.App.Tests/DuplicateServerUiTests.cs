using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;
using TempDirectory = RealmPortal.Tests.Support.TempDirectory;

namespace RealmPortal.App.Tests;

/// <summary>Duplicate… on a server card (#191): prefilled add form; the copy is independent of the original.</summary>
public sealed class DuplicateServerUiTests
{
    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("tests do not launch");
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Backups = _temp.Combine("backups");
            Home = _temp.Combine("home");
            foreach (var d in new[] { Config, Projection, Backups, Home })
                Directory.CreateDirectory(d);
            Session = TestSessions.Create(Config, Projection, Backups, Home, new NoLaunch(), new NeverRunningGameProbe(), seedPreferences: false);
            Loc.Instance.SetLanguage(Language.En);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Backups { get; }
        public string Home { get; }
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
    public void Duplicate_OpensFormPrefilled_AndSaveCreatesIndependentCopy()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        var original = env.Session.Service.AddServer(new NewServer(
            "Demo Realm",
            "logon.example.com",
            Realm: "Icecrown",
            Account: "demo",
            ClientId: cid,
            RequiredClientBuild: 12340,
            ExtraRealmlistLines: ["set patchlist patch.example.net"],
            ClearCacheOnSwitch: true)).Value!;
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));

        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        var card = Assert.Single(main.Servers.Visible);
        Assert.True(card.DuplicateCommand.CanExecute(null));

        card.DuplicateCommand.Execute(null);

        var form = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.True(form.IsDuplicating);
        Assert.False(form.IsEditing);
        Assert.Equal("Demo Realm (copy)", form.Name);
        Assert.Equal("logon.example.com", form.Address);
        Assert.Equal("Icecrown", form.Realm);
        Assert.Equal("demo", form.Account);
        Assert.Equal(12340, form.SelectedBuild?.Build);
        Assert.True(form.ClearCacheOnSwitch);
        Assert.Equal("Duplicate server", form.FormTitle);

        form.Realm = "PvE";
        form.SaveCommand.Execute(null);

        Assert.Null(main.Adding);
        var listed = env.Session.Service.ListServers(compatibleOnly: false).Value!;
        Assert.Equal(2, listed.Count);
        var copy = Assert.Single(listed, s => s.Id != original.Id);
        Assert.Equal("Demo Realm (copy)", copy.Name);
        Assert.Equal("logon.example.com", copy.Host);
        Assert.Equal("PvE", copy.Realm);
        Assert.Equal("demo", copy.Account);
        Assert.Equal(12340, copy.RequiredClientBuild);
        Assert.True(copy.ClearCacheOnSwitch);
        Assert.Contains(copy.ExtraRealmlistLines, line => line.Contains("patch.example.net", StringComparison.Ordinal));
        Assert.NotEqual(original.Id, copy.Id);

        // Editing the copy does not change the original.
        env.Session.Service.UpdateServer(new UpdateServerRequest(copy.Id, "Renamed Copy", copy.Host, "Other", Account: "alt", ClientId: cid));
        var again = env.Session.Service.ListServers(compatibleOnly: false).Value!;
        var stillOriginal = Assert.Single(again, s => s.Id == original.Id);
        Assert.Equal("Demo Realm", stillOriginal.Name);
        Assert.Equal("Icecrown", stillOriginal.Realm);
        Assert.Equal("demo", stillOriginal.Account);
    }

    [AvaloniaFact]
    public void Duplicate_SpanishNameUsesCopiaSuffix()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Mi Servidor", "play.example.net", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("es", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        Loc.Instance.SetLanguage(Language.Es);

        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        Assert.Single(main.Servers.Visible).DuplicateCommand.Execute(null);

        var form = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.Equal("Mi Servidor (copia)", form.Name);
        Assert.Equal("Duplicar servidor", form.FormTitle);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void Duplicate_MenuItem_IsReachableWithAccessibleName()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        var window = new MainWindow(main) { Width = 960, Height = 600 };
        window.Show();
        Screenshots.Settle();
        var split = window.GetVisualDescendants().OfType<SplitButton>().First();
        split.Flyout!.ShowAt(split);
        Screenshots.Settle();
        var menu = ((MenuFlyout)split.Flyout).Items.OfType<MenuItem>().First(m => m.Name == "MenuDuplicate");
        Assert.Equal("Duplicate…", Avalonia.Automation.AutomationProperties.GetName(menu));
        Assert.True(menu.IsVisible);
        window.Close();
    }
}
