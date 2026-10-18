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

    private sealed class ThrowingLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) =>
            throw new InvalidOperationException("Injected process start failure.");
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env(bool throwOnLaunch = false)
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
            IGameLaunchService launch = throwOnLaunch ? new ThrowingLaunch() : Launch;
            Session = PortalSession.CreateForPaths(Config, Projection, Backups, launch);
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
    public async Task PortalTest_DoesNotClaimASuccessfulConnection()
    {
        using var env = new Env();
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        add.Address = "logon.example.org:3724";
        await add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.None, add.TestState);
        Assert.False(add.HasResult);
        Assert.Equal(string.Empty, add.ResultText);
    }

    [AvaloniaFact]
    public void PortalSave_FailureShowsSaveErrorWithoutAFailedConnectionTest()
    {
        using var env = new Env();
        env.AddServer("Hand", "hand.example.org");
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        add.Name = "Hand";
        add.Address = "other.example.org";
        add.SelectedClient = env.Main.Clients.Items[0];
        add.SaveCommand.Execute(null);

        Assert.Equal(ConnectionTest.None, add.TestState);
        Assert.False(add.HasResult);
        Assert.Equal(Loc.Format("error.servernametaken.body", "Hand"), add.SaveError);
        Assert.DoesNotContain("No response", add.SaveError, StringComparison.OrdinalIgnoreCase);
        Assert.True(add.HasSaveError);

        add.Name = "Other";
        Assert.Null(add.SaveError);
        Assert.False(add.HasSaveError);
    }

    [AvaloniaFact]
    public void Reload_UsesEachServersEffectiveClientForVersionAndCompatibility()
    {
        using var env = new Env();
        var fallbackId = env.Session.ResolveClientId()!;
        var classicRoot = Path.Combine(Path.GetDirectoryName(env.Client)!, "classic");
        Directory.CreateDirectory(classicRoot);
        File.WriteAllBytes(Path.Combine(classicRoot, "Wow.exe"), SyntheticPe.WithVersion(new Version(1, 12, 1, 5875)));
        var classicId = env.Session.Service.AddClient(classicRoot).Value!.Id;

        var classic = env.Session.Service.AddServer(new NewServer(
            "Classic",
            "classic.example.org",
            RequiredClientBuild: 5875,
            ClientId: classicId)).Value!;
        var wotlk = env.Session.Service.AddServer(new NewServer(
            "Wotlk",
            "wotlk.example.org",
            RequiredClientBuild: 12340,
            ClientId: fallbackId)).Value!;
        var unpinned = env.Session.Service.AddServer(new NewServer(
            "Unpinned",
            "unpinned.example.org",
            RequiredClientBuild: 5875)).Value!;

        env.Main.Servers.ReloadFromService();
        var classicCard = env.Main.Servers.All.Single(s => s.ServerId == classic.Id);
        var wotlkCard = env.Main.Servers.All.Single(s => s.ServerId == wotlk.Id);
        var unpinnedCard = env.Main.Servers.All.Single(s => s.ServerId == unpinned.Id);

        Assert.Equal(classicId, classicCard.PreferredClientId);
        Assert.True(classicCard.IsCompatible);
        Assert.Contains("5875", classicCard.ClientVersion);
        Assert.DoesNotContain("12340", classicCard.ClientVersion);

        Assert.Equal(fallbackId, wotlkCard.PreferredClientId);
        Assert.True(wotlkCard.IsCompatible);
        Assert.Contains("12340", wotlkCard.ClientVersion);

        Assert.Null(unpinnedCard.PreferredClientId);
        Assert.False(unpinnedCard.IsCompatible);
        Assert.Contains("12340", unpinnedCard.ClientVersion);
        Assert.Equal(fallbackId, env.Session.ResolveClientId(unpinnedCard.PreferredClientId));
    }

    [AvaloniaFact]
    public void ManualAdd_AppearsInList()
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
        Assert.NotNull(env.Main.Dialog);
        env.Main.Dialog!.ConfirmCommand.Execute(null);
        await Task.Delay(200);
        Assert.False(env.Session.Service.GetStatus().Value!.CanUndo);
    }

    [AvaloniaFact]
    public async Task Play_LaunchFailedAfterApply_KeepsNewServerActive_ShowsAppliedError_AndUndo()
    {
        using var env = new Env(throwOnLaunch: true);
        var a = env.AddServer("A", "a.example.org");
        var b = env.AddServer("B", "b.example.org");
        env.Main.Servers.ReloadFromService();
        var first = env.Main.Servers.All.First(s => s.ServerId == a.Id);
        var second = env.Main.Servers.All.First(s => s.ServerId == b.Id);

        await env.Main.Servers.SwitchOnlyAsync(first);
        Assert.Equal(a.Id, env.Main.Servers.Active?.ServerId);

        await env.Main.Servers.PlayAsync(second);

        Assert.Equal(ServersPhase.Idle, env.Main.Servers.Phase);
        Assert.Equal(b.Id, env.Main.Servers.Active?.ServerId);
        Assert.Equal(b.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.Equal("The game didn't start", env.Main.Servers.BannerHeader);
        Assert.Contains(
            "The server was applied and is now active, but the game didn't start. Check the launcher settings and try again.",
            env.Main.Servers.BannerBody);
        Assert.Contains(
            "The server was applied and is now active, but the game didn't start. Check the launcher settings and try again.",
            env.Main.Servers.StatusText);
        Assert.True(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.True(env.Main.Servers.ToastVisible);

        await ((IAsyncRelayCommand)env.Main.Servers.UndoCommand).ExecuteAsync(null);
        Assert.Equal(a.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.Equal(a.Id, env.Main.Servers.Active?.ServerId);
        Assert.False(env.Main.Servers.ToastVisible);
    }

    [AvaloniaFact]
    public async Task Play_LaunchFailedAfterApply_NoUndoToast_WhenCannotUndo()
    {
        using var env = new Env(throwOnLaunch: true);
        var only = env.AddServer("Only", "only.example.org");
        var clientId = env.Session.ResolveClientId()!;
        Assert.True(env.Session.Service.SwitchServer(new SwitchRequest(clientId, only.Id)).Succeeded);
        var discarded = env.Session.Service.DiscardUndo();
        Assert.True(discarded.Succeeded);
        Assert.False(env.Session.Service.GetStatus().Value!.CanUndo);

        env.Main.Servers.ReloadFromService();
        var card = env.Main.Servers.All.First(s => s.ServerId == only.Id);
        await env.Main.Servers.PlayAsync(card);

        Assert.Equal(only.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.Equal(only.Id, env.Main.Servers.Active?.ServerId);
        Assert.False(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.Contains(
            "The server was applied and is now active, but the game didn't start. Check the launcher settings and try again.",
            env.Main.Servers.StatusText);
        Assert.False(env.Main.Servers.ToastVisible);
    }

    [AvaloniaFact]
    public void AppliedLaunchFailed_UsesDedicatedStringsInBothLanguages()
    {
        var applied = new ServiceError(
            ServiceErrorCode.LaunchFailed,
            Consistency: Consistency.AppliedLaunchFailed,
            Diagnostic: "detail");
        var generic = new ServiceError(ServiceErrorCode.LaunchFailed, Diagnostic: "detail");
        const string enBody =
            "The server was applied and is now active, but the game didn't start. Check the launcher settings and try again.";
        const string esBody =
            "El servidor quedó aplicado y activo, pero el juego no se abrió. Revisa los ajustes del lanzador e inténtalo de nuevo.";

        Loc.Instance.SetLanguage(Language.En);
        Assert.Equal("The game didn't start", ErrorText.Title(applied));
        Assert.Equal(enBody, ErrorText.Body(applied));
        Assert.Equal(enBody, ErrorText.Body(applied with { Diagnostic = string.Empty }));
        Assert.Equal("Could not start the game", ErrorText.Title(generic));
        Assert.DoesNotContain(enBody, ErrorText.Body(generic));

        Loc.Instance.SetLanguage(Language.Es);
        Assert.Equal("El juego no se abrió", ErrorText.Title(applied));
        Assert.Equal(esBody, ErrorText.Body(applied));
        Assert.Equal(esBody, ErrorText.Body(applied with { Diagnostic = string.Empty }));
        Assert.Equal("No se pudo iniciar el juego", ErrorText.Title(generic));

        Loc.Instance.SetLanguage(Language.En);
    }
}
