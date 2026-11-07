using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.App.Views;
using RealmPortal.Core.Service;

namespace RealmPortal.App.Tests;

public sealed class ClearErrorsUiTests
{
    public static TheoryData<ServiceErrorCode, string?, ServiceErrorAction> Actions => new()
    {
        { ServiceErrorCode.BackupNotFound, null, ServiceErrorAction.None },
        { ServiceErrorCode.StoreLocked, null, ServiceErrorAction.TryAgain },
        { ServiceErrorCode.GameRunning, null, ServiceErrorAction.CloseGame },
        { ServiceErrorCode.ServerNotFound, null, ServiceErrorAction.ChooseServer },
        { ServiceErrorCode.ClientNotFound, null, ServiceErrorAction.ScanOrAddClient },
        { ServiceErrorCode.PathNotFound, null, ServiceErrorAction.ChooseClientFolder },
        { ServiceErrorCode.InvalidServerInput, null, ServiceErrorAction.FixInput },
        { ServiceErrorCode.IncompatibleClient, null, ServiceErrorAction.UseCompatibleClient },
        { ServiceErrorCode.StoreCorrupt, "/home/demo/servers.json", ServiceErrorAction.RestoreConfigBackup },
        { ServiceErrorCode.PermissionDenied, null, ServiceErrorAction.CheckPermissions },
        { ServiceErrorCode.DiskFull, null, ServiceErrorAction.FreeDiskSpace },
        { ServiceErrorCode.UndoConflict, null, ServiceErrorAction.ResolveUndoConflict },
        { ServiceErrorCode.InconsistentState, null, ServiceErrorAction.ReviewClientFiles },
        { ServiceErrorCode.LaunchFailed, null, ServiceErrorAction.CheckLauncherSettings },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Actions))]
    public async Task EveryActionHasLocalizedHintAndInvokesItsContext(ServiceErrorCode code, string? path, ServiceErrorAction action)
    {
        var recovery = new ErrorRecoveryViewModel();
        var retries = 0;
        var fixes = 0;
        RecoveryDestination? destination = null;
        recovery.NavigateRequested += target => { destination = target; return Task.CompletedTask; };
        recovery.CanRestoreConfig = _ => true;
        recovery.Show(new ServiceError(code, Path: path, ConflictingFiles: ["realmlist.wtf"]),
            () => { retries++; return Task.CompletedTask; },
            () => { fixes++; return Task.CompletedTask; },
            () => Task.CompletedTask);
        Assert.Equal(action, recovery.Action);
        var view = new ErrorRecoveryView { DataContext = recovery };
        var window = new Window { Content = view, Width = 520, Height = 280 };
        window.Show();
        try
        {
            string? previous = null;
            foreach (var language in new[] { Language.En, Language.Es })
            {
                Loc.Instance.SetLanguage(language);
                recovery.RefreshText();
                window.UpdateLayout();
                var hint = view.FindControl<TextBlock>("RecoveryHint")!;
                Assert.Equal(recovery.Hint, hint.Text);
                Assert.DoesNotContain("!", hint.Text!);
                if (action != ServiceErrorAction.None)
                {
                    Assert.False(string.IsNullOrWhiteSpace(hint.Text));
                    if (previous is not null) Assert.NotEqual(previous, hint.Text);
                }
                previous = hint.Text;
            }
            if (action == ServiceErrorAction.None)
            {
                Assert.False(recovery.HasPrimary);
                return;
            }
            var button = view.FindControl<Button>("RecoveryPrimary")!;
            Assert.True(button.IsVisible);
            Assert.True(button.Focusable);
            Assert.Equal(recovery.PrimaryText, AutomationProperties.GetName(button));
            Assert.Equal(recovery.Hint, AutomationProperties.GetHelpText(button));
            await recovery.PrimaryCommand.ExecuteAsync(null);
            switch (action)
            {
                case ServiceErrorAction.TryAgain:
                case ServiceErrorAction.CloseGame:
                case ServiceErrorAction.CheckPermissions:
                case ServiceErrorAction.FreeDiskSpace:
                case ServiceErrorAction.ResolveUndoConflict:
                    Assert.Equal(1, retries); break;
                case ServiceErrorAction.FixInput:
                case ServiceErrorAction.ChooseClientFolder:
                    Assert.Equal(1, fixes); break;
                case ServiceErrorAction.ChooseServer:
                    Assert.Equal(RecoveryDestination.Servers, destination); break;
                case ServiceErrorAction.ScanOrAddClient:
                    Assert.Equal(RecoveryDestination.ScanClients, destination); break;
                case ServiceErrorAction.RestoreConfigBackup:
                    Assert.Equal(RecoveryDestination.ConfigBackups, destination); break;
                case ServiceErrorAction.CheckLauncherSettings:
                    Assert.Equal(RecoveryDestination.LauncherSettings, destination); break;
                default:
                    Assert.Equal(RecoveryDestination.Clients, destination); break;
            }
        }
        finally { window.Close(); Loc.Instance.SetLanguage(Language.En); }
    }

    [AvaloniaFact]
    public async Task SecondaryClientAndUndoActionsAreNotThePrimaryOperation()
    {
        var recovery = new ErrorRecoveryViewModel();
        RecoveryDestination? destination = null;
        recovery.NavigateRequested += target => { destination = target; return Task.CompletedTask; };
        recovery.Show(new ServiceError(ServiceErrorCode.ClientNotFound));
        await recovery.SecondaryCommand.ExecuteAsync(null);
        Assert.Equal(RecoveryDestination.AddClient, destination);
        var discarded = 0;
        recovery.Show(new ServiceError(ServiceErrorCode.UndoConflict), discard: () => { discarded++; return Task.CompletedTask; });
        await recovery.SecondaryCommand.ExecuteAsync(null);
        Assert.Equal(1, discarded);
        recovery.Clear();
        Assert.False(recovery.HasPrimary);
        Assert.False(recovery.HasSecondary);
    }

    [AvaloniaFact]
    public void RestoreOnlyOfferedForAValidBackupAndNeverForStateJson()
    {
        var recovery = new ErrorRecoveryViewModel();
        recovery.Show(new ServiceError(ServiceErrorCode.StoreCorrupt, Path: "/home/demo/servers.json"));
        Assert.False(recovery.HasPrimary);
        recovery.CanRestoreConfig = _ => false;
        recovery.RefreshText();
        Assert.False(recovery.HasPrimary);
        recovery.CanRestoreConfig = _ => true;
        recovery.RefreshText();
        Assert.True(recovery.HasPrimary);
        recovery.Show(new ServiceError(ServiceErrorCode.StoreCorrupt, Path: "/home/demo/state.json"));
        Assert.False(recovery.HasPrimary);
    }

    [AvaloniaFact]
    public async Task RealClientFailureRetriesTheFolderPickerAndClearsRecoveryOnSuccess()
    {
        using var env = new IsolatedAppSession();
        var picks = 0;
        var folder = env.CreateClientFolder("demo-client");
        var clients = new ClientsViewModel(env.Session, () => Task.FromResult<string?>(++picks == 1 ? env.Temp.Combine("missing") : folder), false);
        await clients.PickFolderCommand.ExecuteAsync(null);
        Assert.Equal(ServiceErrorAction.ChooseClientFolder, clients.Recovery.Action);
        var view = new ClientsView { DataContext = clients };
        var window = new Window { Content = view, Width = 720, Height = 440 };
        window.Show();
        try
        {
            var button = view.GetVisualDescendants().OfType<ErrorRecoveryView>().Single().FindControl<Button>("RecoveryPrimary")!;
            Assert.True(button.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            for (var i = 0; i < 100 && clients.Items.Count == 0; i++) await Task.Delay(10);
            Assert.Equal(2, picks);
            Assert.Single(clients.Items);
            Assert.False(clients.Recovery.IsVisible);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(720, Language.En)]
    [InlineData(720, Language.Es)]
    [InlineData(960, Language.En)]
    [InlineData(960, Language.Es)]
    public async Task EmptyScreensExplainCauseAndExposeWorkingActions(int width, Language language)
    {
        using var env = new IsolatedAppSession(languageCode: language == Language.Es ? "es" : "en");
        var main = new MainViewModel(env.Session, autoTick: false);
        var window = new MainWindow(main) { Width = width, Height = 600 };
        window.Show();
        try
        {
            Screenshots.Settle();
            Assert.True(main.Servers.ShowEmpty);
            Assert.Contains(Loc.Get("servers.empty.first.title"), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains(Loc.Get("servers.empty.first.hint"), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            main.Servers.RequestAddServerCommand.Execute(null);
            Screenshots.Settle();
            Assert.Contains(Loc.Get("add.clients.empty"), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            main.Adding!.CancelCommand.Execute(null);
            main.Servers.ScanClientsCommand.Execute(null);
            for (var i = 0; i < 100 && (main.SectionIndex != 1 || main.Clients.IsScanning); i++) await Task.Delay(10);
            Screenshots.Settle();
            Assert.Equal(1, main.SectionIndex);
            Assert.Contains(Loc.Get("clients.empty.cause"), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            main.SectionIndex = 2;
            main.Settings.OpenBackupsManagerCommand.Execute(null);
            Screenshots.Settle();
            Assert.IsType<BackupsViewModel>(main.Content);
            Assert.Contains(Loc.Get("backups.empty.cause"), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            var backupsView = window.GetVisualDescendants().OfType<BackupsView>().Single();
            var emptyCard = Assert.IsType<ContentControl>(backupsView.FindControl<Control>("EmptyNotice"));
            Assert.Contains("card", emptyCard.Classes);
            Assert.True(emptyCard.IsEffectivelyVisible);
            var findClients = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BackupFindClientsButton");
            Assert.True(findClients.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Screenshots.Settle();
            Assert.Equal(1, main.SectionIndex);
        }
        finally { window.Close(); main.DisposeNetwork(); Loc.Instance.SetLanguage(Language.En); }
    }

    [AvaloniaTheory]
    [InlineData(RecoveryDestination.Servers, 0)]
    [InlineData(RecoveryDestination.Clients, 1)]
    [InlineData(RecoveryDestination.ConfigBackups, 2)]
    [InlineData(RecoveryDestination.LauncherSettings, 2)]
    public async Task RecoveryNavigatesToTheRealScreen(RecoveryDestination destination, int section)
    {
        using var env = new IsolatedAppSession();
        var client = env.Session.Service.AddClient(env.CreateClientFolder("demo-client")).Value!;
        env.Session.Service.AddServer(new NewServer("Alpha", "play.example.com", ClientId: client.Id));
        env.Session.Service.AddServer(new NewServer("Beta", "play.example.net", ClientId: client.Id));
        if (destination == RecoveryDestination.ConfigBackups)
            File.WriteAllText(Path.Combine(env.Config, "servers.json"), "{ damaged");
        var main = new MainViewModel(env.Session, autoTick: false);
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        try
        {
            var code = destination switch
            {
                RecoveryDestination.Servers => ServiceErrorCode.ServerNotFound,
                RecoveryDestination.Clients => ServiceErrorCode.IncompatibleClient,
                RecoveryDestination.ConfigBackups => ServiceErrorCode.StoreCorrupt,
                _ => ServiceErrorCode.LaunchFailed,
            };
            main.Clients.Recovery.Show(new ServiceError(code, Path: Path.Combine(env.Config, "servers.json")));
            await main.Clients.Recovery.PrimaryCommand.ExecuteAsync(null);
            Screenshots.Settle();
            Assert.Equal(section, main.SectionIndex);
            Assert.Same(section == 0 ? (ViewModelBase)main.Servers : section == 1 ? main.Clients : main.Settings, main.Content);
            if (destination == RecoveryDestination.LauncherSettings)
                Assert.True(window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "TemplateBox").IsFocused);
            if (destination == RecoveryDestination.ConfigBackups)
            {
                var settings = window.GetVisualDescendants().OfType<SettingsView>().Single();
                var list = settings.FindControl<ItemsControl>("ConfigBackupList")!;
                var scroller = settings.FindControl<ScrollViewer>("Scroller")!;
                Assert.True(scroller.Offset.Y > 0);
                Assert.Contains(main.Settings.ConfigBackups, row => row.CanRestore);
            }
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    private sealed class ToggleProbe : RealmPortal.Core.ClientFiles.IGameProcessProbe
    {
        public bool Fail { get; set; }
        public bool IsGameRunning(string root) => Fail ? throw new IOException("Synthetic probe failure") : false;
    }

    [AvaloniaFact]
    public async Task RetryAfterClosingActuallyRerunsTheFailedSwitchNotAStaleCard()
    {
        var probe = new ToggleProbe();
        using var env = new IsolatedAppSession(probe: probe);
        var client = env.Session.Service.AddClient(env.CreateClientFolder("demo-client")).Value!;
        env.Session.Service.AddServer(new NewServer("Alpha", "play.example.com", ClientId: client.Id));
        var servers = new ServersViewModel(env.Session, autoTick: false);
        probe.Fail = true;
        await servers.SwitchOnlyAsync(servers.Visible.Single());
        Assert.Equal(ServiceErrorAction.CloseGame, servers.Recovery.Action);
        probe.Fail = false;
        await servers.Recovery.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal("Alpha", env.Session.Service.GetStatus().Value!.Active?.Name);
    }

    [AvaloniaFact]
    public async Task FormFixActionFocusesTheServiceField()
    {
        using var env = new IsolatedAppSession();
        var client = env.Session.Service.AddClient(env.CreateClientFolder("demo-client")).Value!;
        env.Session.Service.AddServer(new NewServer("Alpha", "play.example.com", ClientId: client.Id));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.RequestAddServerCommand.Execute(null);
        var form = main.Adding!;
        form.Name = "Alpha";
        form.Address = "play.example.org";
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        try
        {
            form.SaveCommand.Execute(null);
            Assert.Equal(ServiceErrorAction.FixInput, form.Recovery.Action);
            await form.Recovery.PrimaryCommand.ExecuteAsync(null);
            Screenshots.Settle();
            Assert.True(window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "NameBox").IsFocused);
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    [AvaloniaFact]
    public async Task RecoveryDoesNotDiscardUnsavedSettingsWhenNavigationIsCancelled()
    {
        using var env = new IsolatedAppSession();
        var main = new MainViewModel(env.Session, autoTick: false);
        main.SectionIndex = 2;
        main.Settings.Arguments = "--demo";
        main.Settings.Recovery.Show(new ServiceError(ServiceErrorCode.ClientNotFound));
        var navigation = main.Settings.Recovery.PrimaryCommand.ExecuteAsync(null);
        Assert.NotNull(main.Dialog);
        main.Dialog.CancelCommand.Execute(null);
        await navigation;
        Assert.Equal(2, main.SectionIndex);
        Assert.Equal("--demo", main.Settings.Arguments);
        Assert.True(main.Settings.IsDirty);
        main.DisposeNetwork();
    }

    [AvaloniaFact]
    public async Task ErrorRowsUpdateThroughBothRealLanguageControls()
    {
        using var env = new IsolatedAppSession();
        var main = new MainViewModel(env.Session, () => Task.FromResult<string?>(env.Temp.Combine("missing")), autoTick: false);
        main.SectionIndex = 1;
        await main.Clients.PickFolderCommand.ExecuteAsync(null);
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        try
        {
            foreach (var useSettings in new[] { false, true })
            foreach (var language in new[] { Language.Es, Language.En })
            {
                if (useSettings) main.Settings.IsSpanish = language == Language.Es;
                else main.ToggleLanguageCommand.Execute(null);
                Screenshots.Settle();
                var recovery = window.GetVisualDescendants().OfType<ErrorRecoveryView>().Single();
                Assert.Equal(Loc.Get("recovery.chooseclientfolder.hint"), recovery.FindControl<TextBlock>("RecoveryHint")!.Text);
                Assert.Contains(ErrorText.Body(new ServiceError(ServiceErrorCode.PathNotFound, Path: env.Temp.Combine("missing"))), main.Clients.Notice!);
            }
        }
        finally { window.Close(); main.DisposeNetwork(); Loc.Instance.SetLanguage(Language.En); }
    }

    private sealed class FailingSettingsStore : RealmPortal.App.Settings.ISettingsStore
    {
        public RealmPortal.App.Settings.SettingsModel Load() => new RealmPortal.App.Demo.DemoSettingsStore().Load();
        public RealmPortal.App.Settings.SettingsError? Save(RealmPortal.App.Settings.SettingsModel settings) => new("PermissionDenied", "/home/demo/config");
    }

    [AvaloniaFact]
    public void SettingsSaveKeepsTheActualServiceCauseAndNextStep()
    {
        var settings = new SettingsViewModel(new FailingSettingsStore());
        settings.Arguments = "--demo";
        settings.SaveCommand.Execute(null);
        Assert.Equal(ServiceErrorAction.CheckPermissions, settings.Recovery.Action);
        Assert.True(settings.Recovery.HasCause);
        Assert.Equal("/home/demo/config", settings.Recovery.PathText);
        Assert.DoesNotContain("/home/demo/config", settings.Recovery.Cause);
        Assert.True(settings.Recovery.HasPrimary);
    }

    public class RejectingServerService : System.Reflection.DispatchProxy
    {
        public IPortalService Inner { get; set; } = null!;
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(IPortalService.AddServer)
                ? ServiceResult<ServerEntry>.Fail(new ServiceError(ServiceErrorCode.InvalidRealmlist,
                    Field: "ExtraRealmlistLines", ExtraLineIndex: 0))
                : method.Invoke(Inner, args);
    }

    [AvaloniaFact]
    public async Task InvalidExtraLineActionExpandsAndSelectsTheReportedLine()
    {
        using var env = new IsolatedAppSession();
        env.Session.Service.AddClient(env.CreateClientFolder("demo-client", new Version(6, 2, 4, 20779)));
        var service = System.Reflection.DispatchProxy.Create<IPortalService, RejectingServerService>();
        ((RejectingServerService)service).Inner = env.Session.Service;
        var session = new RealmPortal.App.Portal.PortalSession(service, env.Session.UiPreferences,
            env.Session.GetVariable, env.Config, env.Session.NetworkSettings, env.Session.UpdateChecker);
        var main = new MainViewModel(session, autoTick: false);
        main.Servers.RequestAddServerCommand.Execute(null);
        var form = main.Adding!;
        form.Name = "Alpha";
        form.Address = "play.example.com";
        form.ExtraRealmlistLinesText = "\nset patchlist patch.example.com";
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        try
        {
            form.SaveCommand.Execute(null);
            Assert.Equal(ServiceErrorAction.FixInput, form.Recovery.Action);
            form.IsAdvancedExpanded = false;
            await form.Recovery.PrimaryCommand.ExecuteAsync(null);
            Screenshots.Settle();
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "ExtraLinesBox");
            Assert.True(form.IsAdvancedExpanded);
            Assert.True(box.IsFocused);
            Assert.Equal("set patchlist patch.example.com", box.SelectedText);
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    [AvaloniaFact]
    public void AFailedListKeepsItsNoticeAboveTheEmptyClientActions()
    {
        using var env = new IsolatedAppSession();
        env.Session.Service.AddClient(env.CreateClientFolder("demo-client"));
        File.WriteAllText(Path.Combine(env.Config, "clients.json"), "{ damaged");
        var clients = new ClientsViewModel(env.Session, startFirstScan: false);
        Assert.True(clients.HasNotice);
        Assert.True(clients.Recovery.HasCause);
        Assert.True(clients.IsEmpty);
    }

    [AvaloniaFact]
    public void ActionTableCoversTheWholeEnum()
    {
        Assert.Equal(Enum.GetValues<ServiceErrorAction>().Order(), Actions.Select(row => (ServiceErrorAction)row[2]).Order());
    }

    [AvaloniaFact]
    public void ANewServiceErrorHidesThePreviouslyVisibleEmptyPanel()
    {
        using var env = new IsolatedAppSession();
        var main = new MainViewModel(env.Session, autoTick: false);
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var view = window.GetVisualDescendants().OfType<ServersView>().Single();
            Assert.True(view.FindControl<StackPanel>("EmptyState")!.IsVisible);
            File.WriteAllText(Path.Combine(env.Config, "servers.json"), "{ damaged");
            main.Servers.ReloadFromService();
            Screenshots.Settle();
            Assert.False(view.FindControl<StackPanel>("EmptyState")!.IsVisible);
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    [AvaloniaFact]
    public void LongErrorStatusLeavesTheLanguageControlReachable()
    {
        using var env = new IsolatedAppSession();
        var main = new MainViewModel(env.Session, autoTick: false);
        main.SectionIndex = 1;
        main.Clients.Notice = new string('x', 500);
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var text = window.FindControl<TextBlock>("StatusText")!;
            var language = window.FindControl<Button>("LanguageButton")!;
            var textRight = text.TranslatePoint(new Avalonia.Point(text.Bounds.Width, 0), window)!.Value.X;
            var languageLeft = language.TranslatePoint(default, window)!.Value.X;
            Assert.True(textRight <= languageLeft, $"status ends at {textRight}, language begins at {languageLeft}");
            Assert.Equal(main.StatusText, ToolTip.GetTip(text));
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    [AvaloniaTheory]
    [InlineData("name", "add.name")]
    [InlineData("host", "add.address")]
    [InlineData("realm", "add.realm")]
    [InlineData("account", "add.account")]
    [InlineData("build", "edit.build")]
    [InlineData("clientId", "add.client")]
    public void InvalidFieldCauseUsesTheVisibleLocalizedLabel(string field, string labelKey)
    {
        foreach (var language in new[] { Language.Es, Language.En })
        {
            Loc.Instance.SetLanguage(language);
            Assert.Contains(Loc.Get(labelKey), ErrorText.Body(new ServiceError(ServiceErrorCode.InvalidServerInput, Field: field)));
        }
        Loc.Instance.SetLanguage(Language.En);
    }

}
