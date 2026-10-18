using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Settings;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
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

    /// <summary>Play's file apply checks the probe once. Later checks (the running watch) fail.</summary>
    /// <summary>Returns a fixed probe result and never opens a socket.</summary>
    private sealed class LateProbe : IConnectionProbe
    {
        private readonly TaskCompletionSource<ProbeResult> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public CancellationToken Token { get; private set; }

        public bool SawCancellation { get; private set; }

        public async Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            cancellationToken.Register(() => SawCancellation = true);
            var result = await _gate.Task.ConfigureAwait(false);
            SawCancellation = cancellationToken.IsCancellationRequested;
            return result;
        }

        public void Release(ProbeResult result = ProbeResult.Ok) => _gate.TrySetResult(result);
    }

    private sealed class FakeProbe : IConnectionProbe
    {
        private readonly ProbeResult _result;
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeProbe(ProbeResult result, bool wait = false)
        {
            _result = result;
            if (!wait)
            {
                _gate.TrySetResult();
            }
        }

        public int Calls { get; private set; }

        public string? Host { get; private set; }

        public int Port { get; private set; }

        public TimeSpan Timeout { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            Host = host;
            Port = port;
            Timeout = timeout;
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return _result;
        }
    }

    private sealed class FailAfterFirstProbe : IGameProcessProbe
    {
        private int _calls;

        public bool IsGameRunning(string clientRoot)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                throw new InvalidOperationException("Injected process check failure.");
            }

            return false;
        }
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env(bool throwOnLaunch = false, IGameProcessProbe? probe = null)
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
            if (probe is null)
            {
                Session = PortalSession.CreateForPaths(Config, Projection, Backups, launch);
            }
            else
            {
                var getVariable = (string name) =>
                    name == ClientFilesApplier.BackupDirectoryVariable ? Backups : null;
                var service = new PortalService(Config, Projection, new PeClientVersionReader(), launch, probe, getVariable);
                Session = new PortalSession(service, new UiPreferencesStore(Config), getVariable);
            }
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
        var probe = new FakeProbe(ProbeResult.Failed);
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { }, probe);
        add.Address = "logon.example.org:3724";
        await add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Failed, add.TestState);
        Assert.Equal("No response", add.ResultText);
        Assert.Equal(ServerStatus.NoResponse, add.ResultStatus);
        Assert.Equal(1, probe.Calls);
        Assert.NotEqual(ConnectionTest.Ok, add.TestState);
    }

    [AvaloniaFact]
    public async Task PortalTest_OpenLoopbackPort_IsOk()
    {
        using var env = new Env();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var acceptCancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var accept = listener.AcceptTcpClientAsync(acceptCancel.Token);
            env.Main.Servers.RequestAddServerCommand.Execute(null);
            var add = env.Main.Adding!;
            add.Address = $"127.0.0.1:{port}";
            await add.TestCommand.ExecuteAsync(null);
            Assert.Equal(ConnectionTest.Ok, add.TestState);
            Assert.Equal("Connection OK", add.ResultText);
            Assert.Equal(ServerStatus.Ready, add.ResultStatus);
            using var client = await accept;
            client.Close();
        }
        finally
        {
            listener.Stop();
        }
    }

    [AvaloniaTheory]
    [InlineData("host;rm -rf")]
    [InlineData("http://x")]
    [InlineData("exámple.com")]
    [InlineData("a..b")]
    public async Task PortalTest_AddressRejectedByRealmlist_ShowsErrorAndDoesNotProbe(string address)
    {
        using var env = new Env();
        var probe = new FakeProbe(ProbeResult.Ok);
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { }, probe);
        add.Address = address;
        await add.TestCommand.ExecuteAsync(null);

        Assert.True(add.HasAddressError);
        Assert.Equal(ConnectionTest.None, add.TestState);
        Assert.Equal(0, probe.Calls);
    }

    [AvaloniaFact]
    public async Task PortalTest_BracketedIpv6_IsPassedToTheProbeWithoutBrackets()
    {
        using var env = new Env();
        var probe = new FakeProbe(ProbeResult.Failed);
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { }, probe);
        add.Address = "[::1]:8085";
        await add.TestCommand.ExecuteAsync(null);

        Assert.False(add.HasAddressError);
        Assert.Equal(1, probe.Calls);
        Assert.Equal("::1", probe.Host);
        Assert.Equal(8085, probe.Port);
    }

    [AvaloniaFact]
    public async Task PortalTest_Ipv6Loopback_IsOkWhenTheListenerIsAvailable()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return;
        }

        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
        }
        catch (SocketException)
        {
            return;
        }

        using var env = new Env();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
            add.Address = $"[::1]:{port}";
            await add.TestCommand.ExecuteAsync(null);
            Assert.False(add.HasAddressError);
            Assert.Equal(ConnectionTest.Ok, add.TestState);
            Assert.Equal(ServerStatus.Ready, add.ResultStatus);
        }
        finally
        {
            listener.Stop();
        }
    }

    [AvaloniaFact]
    public async Task PortalTest_ClosedLoopbackPort_IsFailed()
    {
        using var env = new Env();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        add.Address = $"127.0.0.1:{port}";
        await add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Failed, add.TestState);
        Assert.Equal("No response", add.ResultText);
        Assert.Equal(ServerStatus.NoResponse, add.ResultStatus);
    }

    [AvaloniaFact]
    public async Task PortalTest_FakeProbeTimeout_ShowsTestingThenTimedOut_OnDefaultPort()
    {
        using var env = new Env();
        var probe = new FakeProbe(ProbeResult.TimedOut, wait: true);
        var main = new MainViewModel(env.Session, () => Task.FromResult<string?>(env.Client), autoTick: false, probe);
        main.Servers.RequestAddServerCommand.Execute(null);
        var add = main.Adding!;
        add.Address = "127.0.0.1";
        var run = add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Testing, add.TestState);
        Assert.Equal("Testing…", add.ResultText);
        Assert.Equal("Testing connection…", main.StatusText);
        Assert.False(add.TestCommand.CanExecute(null));
        Assert.Equal(1, probe.Calls);
        Assert.Equal("127.0.0.1", probe.Host);
        Assert.Equal(AddServerViewModel.DefaultPort, probe.Port);
        Assert.Equal(3724, probe.Port);
        Assert.Equal(AddServerViewModel.ConnectionProbeTimeout, probe.Timeout);

        probe.Release();
        await run;
        Assert.Equal(ConnectionTest.TimedOut, add.TestState);
        Assert.Equal("Timed out", add.ResultText);
        Assert.Equal(ServerStatus.NoResponse, add.ResultStatus);
        Assert.True(add.HasResult);

        Loc.Instance.SetLanguage(Language.Es);
        try
        {
            add.RefreshText();
            Assert.Equal("Tiempo agotado", add.ResultText);
        }
        finally
        {
            Loc.Instance.SetLanguage(Language.En);
        }
    }

    [AvaloniaFact]
    public async Task PortalTest_ClosingTheForm_IgnoresALateProbeResultAndCancelsIt()
    {
        using var env = new Env();
        var probe = new LateProbe();
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { }, probe);
        add.Address = "logon.example.org";
        var run = add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Testing, add.TestState);
        Assert.Equal(1, probe.Calls);

        add.CancelCommand.Execute(null);
        Assert.True(probe.SawCancellation);
        var stateWhenClosed = add.TestState;

        probe.Release(ProbeResult.Ok);
        await run;
        Assert.Equal(stateWhenClosed, add.TestState);
        Assert.Equal(ConnectionTest.Testing, add.TestState);
    }

    [AvaloniaFact]
    public async Task PortalTest_LeavingTheSection_CancelsTheProbeAndDropsTheLateResult()
    {
        using var env = new Env();
        var probe = new LateProbe();
        var main = new MainViewModel(env.Session, () => Task.FromResult<string?>(env.Client), autoTick: false, probe);
        main.Servers.RequestAddServerCommand.Execute(null);
        var add = main.Adding!;
        add.Address = "logon.example.org";
        var run = add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Testing, add.TestState);

        main.SectionIndex = 1;
        Assert.Null(main.Adding);
        Assert.True(probe.SawCancellation);

        probe.Release(ProbeResult.Ok);
        await run;
        Assert.Equal(ConnectionTest.Testing, add.TestState);
    }

    [AvaloniaFact]
    public async Task PortalTest_SuccessfulSave_CancelsTheProbeAndDropsTheLateResult()
    {
        using var env = new Env();
        var probe = new LateProbe();
        var saved = false;
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => saved = true, () => { }, probe);
        add.Name = "Fresh Realm";
        add.Address = "fresh.example.org";
        add.SelectedClient = env.Main.Clients.Items[0];
        var run = add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Testing, add.TestState);

        add.SaveCommand.Execute(null);
        Assert.True(saved);
        Assert.True(probe.SawCancellation);

        probe.Release(ProbeResult.Failed);
        await run;
        Assert.Equal(ConnectionTest.Testing, add.TestState);
    }

    [AvaloniaFact]
    public async Task PortalTest_AddressChangedWhileProbeIsInFlight_DropsTheOldResult()
    {
        using var env = new Env();
        var probe = new FakeProbe(ProbeResult.Ok, wait: true);
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { }, probe);
        add.Address = "old.example.org";
        var run = add.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Testing, add.TestState);
        Assert.Equal(1, probe.Calls);
        Assert.Equal("old.example.org", probe.Host);

        add.Address = "new.example.org";
        probe.Release();
        await run;

        Assert.Equal("new.example.org", add.Address);
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
    public void PortalSave_SaveErrorFollowsLanguageSwitch()
    {
        using var env = new Env();
        env.AddServer("Hand", "hand.example.org");
        var add = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        add.Name = "Hand";
        add.Address = "other.example.org";
        add.SelectedClient = env.Main.Clients.Items[0];
        add.SaveCommand.Execute(null);

        Assert.Equal(Loc.Format("error.servernametaken.body", "Hand"), add.SaveError);
        var english = add.SaveError;

        Loc.Instance.SetLanguage(Language.Es);
        try
        {
            add.RefreshText();
            Assert.Equal(Loc.Format("error.servernametaken.body", "Hand"), add.SaveError);
            Assert.NotEqual(english, add.SaveError);
            Assert.True(add.HasSaveError);
        }
        finally
        {
            Loc.Instance.SetLanguage(Language.En);
        }
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
    public async Task Play_Success_ShowsRunningOverlayOnReloadedCard()
    {
        using var env = new Env();
        var server = env.AddServer("Play", "play.example.org");
        env.Main.Servers.ReloadFromService();
        var stale = env.Main.Servers.All.First(x => x.ServerId == server.Id);
        await env.Main.Servers.PlayAsync(stale);

        var card = env.Main.Servers.All.Single(x => x.ServerId == server.Id);
        Assert.NotSame(stale, card);
        Assert.Equal(ServerOverlay.Running, card.Overlay);
        Assert.Equal(1, env.Launch.Launches);
    }

    [AvaloniaFact]
    public async Task Play_ProcessCheckFailure_ReturnsToIdleWithError()
    {
        using var env = new Env(probe: new FailAfterFirstProbe());
        var server = env.AddServer("Play", "play.example.org");
        env.Main.Servers.ReloadFromService();
        var card = env.Main.Servers.All.First(x => x.ServerId == server.Id);
        await env.Main.Servers.PlayAsync(card);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (env.Main.Servers.Phase == ServersPhase.Running && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(ServersPhase.Idle, env.Main.Servers.Phase);
        Assert.Equal(server.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.False(env.Main.Servers.GameClosed);
        Assert.True(env.Main.Servers.ShowBanner);
        Assert.Contains("The process list could not be read", env.Main.Servers.BannerBody);
        var visible = env.Main.Servers.All.Single(x => x.ServerId == server.Id);
        Assert.Equal(ServerOverlay.None, visible.Overlay);
        Assert.True(visible.CanAct);
        Assert.True(visible.PlayCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Settings_PersistsLanguageOnlyAfterLauncherAndStorageSucceed()
    {
        using var env = new Env();
        Assert.Equal("en", env.Session.UiPreferences.Load().LanguageCode);
        var store = new PortalSettingsStore(env.Session);
        var environment = new Dictionary<string, string?>();

        var launcherFailed = store.Save(new SettingsModel(
            Language.Es,
            "missing-placeholder",
            [],
            [],
            environment,
            env.Backups));
        Assert.NotNull(launcherFailed);
        Assert.Equal("en", env.Session.UiPreferences.Load().LanguageCode);

        var storageFailed = store.Save(new SettingsModel(
            Language.Es,
            "wine {exe}",
            [],
            [],
            environment,
            "relative/backups"));
        Assert.NotNull(storageFailed);
        Assert.Equal("en", env.Session.UiPreferences.Load().LanguageCode);

        var saved = store.Save(new SettingsModel(
            Language.Es,
            "wine {exe}",
            [],
            [],
            environment,
            env.Backups));
        Assert.Null(saved);
        Assert.Equal("es", env.Session.UiPreferences.Load().LanguageCode);
        Assert.Equal(Language.Es, env.Session.UiPreferences.ResolveLanguage());
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
    public async Task UndoConflict_ShowsRetryAndDiscard_RetrySucceedsAfterTheFileIsFixed_DismissKeepsCanUndo()
    {
        using var env = new Env();
        var a = env.AddServer("A", "a.example.org");
        var b = env.AddServer("B", "b.example.org");
        var realmlist = Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf");
        env.PutRealmlist("set realmlist seed.example.org\r\n");
        Assert.True(env.Session.Service.SwitchServer(new SwitchRequest(env.Session.ResolveClientId()!, a.Id)).Succeeded);
        Assert.True(env.Session.Service.SwitchServer(new SwitchRequest(env.Session.ResolveClientId()!, b.Id)).Succeeded);
        var switched = File.ReadAllBytes(realmlist);
        env.PutRealmlist("set realmlist tamper.example.org\r\n");
        env.Main.Servers.ReloadFromService();

        await ((IAsyncRelayCommand)env.Main.Servers.UndoCommand).ExecuteAsync(null);

        Assert.True(env.Main.Servers.ToastVisible);
        Assert.True(env.Main.Servers.ShowRetryUndo);
        Assert.True(env.Main.Servers.ShowDiscardUndo);
        Assert.Equal(b.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.True(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.Null(env.Main.Dialog);

        env.Main.Servers.DismissToastCommand.Execute(null);
        Assert.False(env.Main.Servers.ToastVisible);
        Assert.False(env.Main.Servers.ShowRetryUndo);
        Assert.True(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.Null(env.Main.Dialog);
        Assert.Equal("set realmlist tamper.example.org\r\n", Encoding.Latin1.GetString(File.ReadAllBytes(realmlist)));

        env.Main.Servers.ReloadFromService();
        File.WriteAllBytes(realmlist, switched);
        await ((IAsyncRelayCommand)env.Main.Servers.UndoCommand).ExecuteAsync(null);

        Assert.Equal(a.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.False(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.False(env.Main.Servers.ToastVisible);
        Assert.False(env.Main.Servers.ShowRetryUndo);
        Assert.False(env.Main.Servers.ShowDiscardUndo);
    }

    [AvaloniaFact]
    public async Task PendingUndo_StaysAfterToastExpires_AndUndoStillWorks()
    {
        using var env = new Env();
        var a = env.AddServer("A", "a.example.org");
        var b = env.AddServer("B", "b.example.org");
        env.Main.Servers.ReloadFromService();
        Assert.False(env.Main.Servers.CanUndoPending);
        Assert.False(env.Main.Servers.UndoCommand.CanExecute(null));

        await env.Main.Servers.SwitchOnlyAsync(env.Main.Servers.All.First(s => s.ServerId == a.Id));
        await env.Main.Servers.SwitchOnlyAsync(env.Main.Servers.All.First(s => s.ServerId == b.Id));
        Assert.True(env.Main.Servers.ToastVisible);
        Assert.True(env.Main.Servers.CanUndoPending);
        Assert.True(env.Main.Servers.UndoCommand.CanExecute(null));

        env.Main.Servers.AdvanceToast(ServersViewModel.ToastDuration);
        Assert.False(env.Main.Servers.ToastVisible);
        Assert.True(env.Main.Servers.CanUndoPending);
        Assert.True(env.Main.Servers.UndoCommand.CanExecute(null));

        await ((IAsyncRelayCommand)env.Main.Servers.UndoCommand).ExecuteAsync(null);
        Assert.Equal(a.Id, env.Session.Service.GetStatus().Value!.Active?.Id);
        Assert.False(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.False(env.Main.Servers.CanUndoPending);
        Assert.False(env.Main.Servers.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task PendingUndo_StaysVisibleAfterSuccessfulPlay()
    {
        using var env = new Env();
        var server = env.AddServer("Play", "play.example.org");
        env.Main.Servers.ReloadFromService();
        var card = env.Main.Servers.All.First(x => x.ServerId == server.Id);
        await env.Main.Servers.PlayAsync(card);

        Assert.Equal(1, env.Launch.Launches);
        Assert.Equal(ServersPhase.Running, env.Main.Servers.Phase);
        Assert.True(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.True(env.Main.Servers.CanUndoPending);
        Assert.False(env.Main.Servers.UndoCommand.CanExecute(null));
        Assert.False(env.Main.Servers.DiscardUndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task UndoAndDiscard_AreDisabledUnlessIdle()
    {
        using var env = new Env();
        var a = env.AddServer("A", "a.example.org");
        var b = env.AddServer("B", "b.example.org");
        env.Main.Servers.ReloadFromService();
        await env.Main.Servers.SwitchOnlyAsync(env.Main.Servers.All.First(s => s.ServerId == a.Id));
        await env.Main.Servers.SwitchOnlyAsync(env.Main.Servers.All.First(s => s.ServerId == b.Id));
        Assert.Equal(ServersPhase.Idle, env.Main.Servers.Phase);
        Assert.True(env.Main.Servers.CanUndoPending);
        Assert.True(env.Main.Servers.UndoCommand.CanExecute(null));
        Assert.True(env.Main.Servers.DiscardUndoCommand.CanExecute(null));

        foreach (var phase in new[] { ServersPhase.Switching, ServersPhase.Opening, ServersPhase.Running })
        {
            env.Main.Servers.Phase = phase;
            Assert.False(env.Main.Servers.UndoCommand.CanExecute(null));
            Assert.False(env.Main.Servers.DiscardUndoCommand.CanExecute(null));
        }

        env.Main.Servers.Phase = ServersPhase.Idle;
        Assert.True(env.Main.Servers.UndoCommand.CanExecute(null));
        Assert.True(env.Main.Servers.DiscardUndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task PendingUndo_SurvivesRestart_AndDisappearsAfterUndoOrDiscard()
    {
        using var env = new Env();
        var a = env.AddServer("A", "a.example.org");
        var b = env.AddServer("B", "b.example.org");
        var clientId = env.Session.ResolveClientId()!;
        Assert.True(env.Session.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.True(env.Session.Service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);

        var restarted = new MainViewModel(
            PortalSession.CreateForPaths(env.Config, env.Projection, env.Backups, env.Launch),
            () => Task.FromResult<string?>(env.Client),
            autoTick: false);
        Assert.True(restarted.Servers.CanUndoPending);
        Assert.True(restarted.Servers.UndoCommand.CanExecute(null));
        Assert.Equal(b.Id, restarted.Servers.Active?.ServerId);

        await ((IAsyncRelayCommand)restarted.Servers.UndoCommand).ExecuteAsync(null);
        Assert.Equal(a.Id, restarted.Servers.Active?.ServerId);
        Assert.False(restarted.Servers.CanUndoPending);
        Assert.False(restarted.Servers.UndoCommand.CanExecute(null));

        Assert.True(env.Session.Service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);
        env.Main.Servers.ReloadFromService();
        Assert.True(env.Main.Servers.CanUndoPending);
        env.Main.Servers.DiscardUndoCommand.Execute(null);
        var shown = DateTime.UtcNow.AddSeconds(5);
        while (env.Main.Dialog is null && DateTime.UtcNow < shown)
        {
            await Task.Delay(20);
        }

        Assert.NotNull(env.Main.Dialog);
        env.Main.Dialog!.ConfirmCommand.Execute(null);
        var cleared = DateTime.UtcNow.AddSeconds(5);
        while (env.Main.Servers.CanUndoPending && DateTime.UtcNow < cleared)
        {
            await Task.Delay(20);
        }

        Assert.False(env.Session.Service.GetStatus().Value!.CanUndo);
        Assert.False(env.Main.Servers.CanUndoPending);
        Assert.False(env.Main.Servers.UndoCommand.CanExecute(null));
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
        await ((IAsyncRelayCommand)env.Main.Servers.UndoCommand).ExecuteAsync(null);
        Assert.True(env.Main.Servers.ShowDiscardUndo);

        var discard = ((IAsyncRelayCommand)env.Main.Servers.DiscardUndoCommand).ExecuteAsync(null);
        var shown = DateTime.UtcNow.AddSeconds(5);
        while (env.Main.Dialog is null && DateTime.UtcNow < shown)
        {
            await Task.Delay(20);
        }

        Assert.NotNull(env.Main.Dialog);
        env.Main.Dialog!.ConfirmCommand.Execute(null);
        await discard;
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
