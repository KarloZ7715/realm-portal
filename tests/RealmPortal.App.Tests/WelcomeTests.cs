using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.App.Views;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;
using TempDirectory = RealmPortal.Tests.Support.TempDirectory;

namespace RealmPortal.App.Tests;

/// <summary>First-run welcome (#107): shown once, language/clients/servers, finished or skipped flag.</summary>
public class WelcomeTests
{
    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Welcome tests do not launch.");
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env(bool welcomeCompleted = false, bool firstScanCompleted = false, string[]? additionalRoots = null)
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Backups = _temp.Combine("backups");
            Home = _temp.Combine("home");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Projection);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Home);

            Session = TestSessions.Create(
                Config,
                Projection,
                Backups,
                Home,
                new NoLaunch(),
                new NeverRunningGameProbe(),
                firstScanCompleted: firstScanCompleted,
                additionalScanRoots: additionalRoots,
                seedPreferences: false);
            Session.UiPreferences.Save(new UiPreferences(
                "en",
                [],
                FirstScanCompleted: firstScanCompleted,
                WelcomeCompleted: welcomeCompleted));
            Loc.Instance.SetLanguage(Language.En);
            Main = new MainViewModel(Session, PickAsync, autoTick: false);
        }

        public string? NextPick { get; set; }

        private Task<string?> PickAsync() => Task.FromResult(NextPick);

        public string Config { get; }
        public string Projection { get; }
        public string Backups { get; }
        public string Home { get; }
        public PortalSession Session { get; }
        public MainViewModel Main { get; }

        public string CreateClient(string name)
        {
            var folder = _temp.Combine(name);
            Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
            File.WriteAllBytes(
                Path.Combine(folder, "Wow.exe"),
                SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            return folder;
        }

        public void Dispose() => _temp.Dispose();
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(predicate(), "Condition was not reached in time.");
    }

    [Fact]
    public void WelcomeCompleted_DefaultsFalse_AndIsWelcomeDoneFollowsFirstScanForUpgrades()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);

        Assert.False(store.Load().IsWelcomeDone);
        Assert.Null(store.Load().WelcomeCompleted);

        store.Save(new UiPreferences("en", FirstScanCompleted: true));
        Assert.True(store.Load().IsWelcomeDone);

        store.Save(new UiPreferences("en", FirstScanCompleted: false, WelcomeCompleted: true));
        Assert.True(store.Load().IsWelcomeDone);

        store.Save(new UiPreferences("en", FirstScanCompleted: false, WelcomeCompleted: false));
        Assert.False(store.Load().IsWelcomeDone);
    }

    [Fact]
    public void LegacyPreferencesWithoutWelcomeFlag_TreatFirstScanAsDone()
    {
        using var temp = new TempDirectory();
        var config = temp.Path;
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "ui-preferences.json"),
            """{"LanguageCode":"en","ClientsInUse":[],"FirstScanCompleted":true}""");

        var loaded = new UiPreferencesStore(config).Load();
        Assert.Null(loaded.WelcomeCompleted);
        Assert.True(loaded.FirstScanCompleted);
        Assert.True(loaded.IsWelcomeDone);
    }

    [AvaloniaFact]
    public void FreshInstall_ShowsWelcome_AndSkipsBackgroundFirstScan()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: false);

        Assert.True(env.Main.IsWelcomeActive);
        Assert.NotNull(env.Main.Welcome);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, env.Main.Welcome!.Step);
        Assert.False(env.Main.Clients.IsScanning);
        Assert.False(env.Session.UiPreferences.Load().IsWelcomeDone);
    }

    [AvaloniaFact]
    public void CompletedWelcome_DoesNotShowAgain()
    {
        using var env = new Env(welcomeCompleted: true, firstScanCompleted: true);

        Assert.False(env.Main.IsWelcomeActive);
        Assert.Null(env.Main.Welcome);
    }

    [AvaloniaFact]
    public void Skip_PersistsFlag_AndHidesWelcomeForever()
    {
        using var env = new Env();
        Assert.True(env.Main.IsWelcomeActive);

        env.Main.Welcome!.SkipCommand.Execute(null);

        Assert.False(env.Main.IsWelcomeActive);
        Assert.True(env.Session.UiPreferences.Load().IsWelcomeDone);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);

        var again = new MainViewModel(env.Session, autoTick: false);
        Assert.False(again.IsWelcomeActive);
    }

    [AvaloniaFact]
    public void LanguageStep_AppliesSpanishAndAdvances()
    {
        using var env = new Env();
        var welcome = env.Main.Welcome!;
        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, welcome.Step);

        welcome.SelectSpanishCommand.Execute(null);
        welcome.ContinueLanguageCommand.Execute(null);

        Assert.Equal(Language.Es, Loc.Instance.Current);
        Assert.Equal("es", env.Session.UiPreferences.Load().LanguageCode);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);
    }

    [AvaloniaFact]
    public void Back_StepsBackOneStep_KeepsLanguage_AndIsUnavailableOnLanguage()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var welcome = env.Main.Welcome!;
        Assert.False(welcome.BackCommand.CanExecute(null));

        welcome.SelectSpanishCommand.Execute(null);
        welcome.ContinueLanguageCommand.Execute(null);
        welcome.ContinueClientsCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);

        welcome.BackCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);
        Assert.True(welcome.IsSpanish);

        welcome.BackCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, welcome.Step);
        Assert.False(welcome.BackCommand.CanExecute(null));
        Assert.True(welcome.IsSpanish);
        Assert.Equal(Language.Es, Loc.Instance.Current);
        Assert.True(env.Main.IsWelcomeActive);
    }

    [AvaloniaFact]
    public async Task ClientsStep_DetectsSyntheticClient_ThenFinishPersists()
    {
        using var temp = new TempDirectory();
        var scanRoot = temp.Combine("scan");
        var game = Path.Combine(scanRoot, "game");
        Directory.CreateDirectory(Path.Combine(game, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(game, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        using var env = new Env(additionalRoots: [scanRoot]);
        var welcome = env.Main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);

        await WaitUntil(() => welcome.Step == WelcomeViewModel.WelcomeStep.Clients && !welcome.IsScanningClients);

        Assert.True(welcome.HasClients);
        Assert.Single(env.Main.Clients.Items);

        welcome.ContinueClientsCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);

        welcome.FinishCommand.Execute(null);
        Assert.False(env.Main.IsWelcomeActive);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);
        Assert.True(env.Session.UiPreferences.Load().FirstScanCompleted);
    }

    [AvaloniaFact]
    public async Task ClientsStep_PickFolder_AddsClient()
    {
        using var env = new Env();
        var folder = env.CreateClient("manual");
        env.NextPick = folder;
        var welcome = env.Main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);
        await WaitUntil(() => welcome.Step == WelcomeViewModel.WelcomeStep.Clients && !welcome.IsScanningClients);

        Assert.True(welcome.NoClients);
        await welcome.PickClientFolderCommand.ExecuteAsync(null);

        Assert.True(welcome.HasClients);
        Assert.Single(env.Main.Clients.Items);
    }

    [AvaloniaFact]
    public void ServersStep_AddServer_DisabledWithoutClients()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        env.Session.UiPreferences.Save(new UiPreferences("en", [], FirstScanCompleted: true, WelcomeCompleted: false));
        var main = new MainViewModel(env.Session, autoTick: false);
        Assert.True(main.IsWelcomeActive);
        var welcome = main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);
        welcome.ContinueClientsCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);
        Assert.False(welcome.AddServerCommand.CanExecute(null));
    }

    /// <summary>A welcome on the Servers step with one synthetic client in use (no scan needed).</summary>
    private static (MainViewModel Main, WelcomeViewModel Welcome) WelcomeOnServersStep(Env env)
    {
        var folder = env.CreateClient("for-add");
        var added = env.Session.Service.AddClient(folder).Value!;
        env.Session.UiPreferences.Save(new UiPreferences(
            "en",
            [added.Id],
            FirstScanCompleted: true,
            WelcomeCompleted: false));
        var main = new MainViewModel(env.Session, autoTick: false);
        Assert.True(main.IsWelcomeActive);
        var welcome = main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);
        welcome.ContinueClientsCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);
        return (main, welcome);
    }

    [AvaloniaFact]
    public void ServersStep_AddTwoServersInARow_StaysInWelcome_ThenFinish()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (main, welcome) = WelcomeOnServersStep(env);
        Assert.True(welcome.AddServerCommand.CanExecute(null));

        foreach (var (name, host) in new[] { ("Demo Realm", "logon.example.com"), ("Second Realm", "play.example.net") })
        {
            welcome.AddServerCommand.Execute(null);

            // Regression (#202): the form opens inside the welcome; the welcome is not completed.
            Assert.True(main.IsWelcomeActive);
            Assert.True(welcome.IsServerFormOpen);
            Assert.False(welcome.IsStepVisible);
            Assert.False(env.Session.UiPreferences.Load().IsWelcomeDone);
            Assert.False(welcome.BackCommand.CanExecute(null));

            var form = welcome.ServerForm!;
            form.Name = name;
            form.Address = host;
            form.SaveCommand.Execute(null);

            Assert.Null(welcome.ServerForm);
            Assert.True(main.IsWelcomeActive);
            Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);
        }

        Assert.Equal(2, welcome.ServerItems.Count);
        Assert.True(welcome.HasServers);
        Assert.Equal("2 servers ready", welcome.Title);
        Assert.False(env.Session.UiPreferences.Load().IsWelcomeDone);

        welcome.FinishCommand.Execute(null);
        Assert.False(main.IsWelcomeActive);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);
        Assert.Equal(2, env.Session.Service.ListServers(compatibleOnly: false).Value!.Count);
    }

    [AvaloniaFact]
    public void ServersStep_AddServerCancel_ReturnsToServersStep()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (main, welcome) = WelcomeOnServersStep(env);

        welcome.AddServerCommand.Execute(null);
        Assert.True(welcome.IsServerFormOpen);
        welcome.ServerForm!.CancelCommand.Execute(null);

        Assert.Null(welcome.ServerForm);
        Assert.True(main.IsWelcomeActive);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);
        Assert.Empty(welcome.ServerItems);
        Assert.False(env.Session.UiPreferences.Load().IsWelcomeDone);
        Assert.True(welcome.AddServerCommand.CanExecute(null));

        welcome.SkipCommand.Execute(null);
        Assert.False(main.IsWelcomeActive);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);
    }

    [AvaloniaFact]
    public void ClientsStep_ContinueLabel_NeverShowsZero()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (main, welcome) = WelcomeOnServersStep(env);
        welcome.BackCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);
        var client = Assert.Single(main.Clients.Items);
        Assert.Equal("Continue with 1", welcome.ContinueClientsLabel);

        var changed = new List<string?>();
        welcome.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        client.IsInUse = false;

        Assert.Contains(nameof(WelcomeViewModel.ContinueClientsLabel), changed);
        Assert.Equal("Continue", welcome.ContinueClientsLabel);
        Assert.DoesNotContain("0", welcome.ContinueClientsLabel);
        Assert.True(welcome.ContinueClientsCommand.CanExecute(null));
        Loc.Instance.SetLanguage(Language.Es);
        Assert.Equal("Continuar", welcome.ContinueClientsLabel);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void ClientsStep_ContinueLabel_WithoutClientsFound()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var welcome = env.Main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);

        Assert.True(welcome.NoClients);
        Assert.Equal("Continue without clients", welcome.ContinueClientsLabel);
    }

    [AvaloniaFact]
    public void Back_IsUnavailableOnLanguageStep()
    {
        using var env = new Env();
        var welcome = env.Main.Welcome!;

        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, welcome.Step);
        Assert.False(welcome.HasBack);
        Assert.False(welcome.BackCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Back_FromServers_KeepsClientSelection_AndFromClients_KeepsLanguage()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (main, welcome) = WelcomeOnServersStep(env);
        var client = Assert.Single(main.Clients.Items);
        Assert.True(client.IsInUse);

        welcome.BackCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);
        Assert.True(Assert.Single(main.Clients.Items).IsInUse);
        Assert.True(welcome.HasBack);

        welcome.BackCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, welcome.Step);
        Assert.True(welcome.IsEnglish);
        Assert.False(welcome.HasBack);
        Assert.False(env.Session.UiPreferences.Load().IsWelcomeDone);

        // Forward again: the language and client choices are still there.
        welcome.ContinueLanguageCommand.Execute(null);
        welcome.ContinueClientsCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);
        Assert.True(Assert.Single(main.Clients.Items).IsInUse);
        Assert.Equal(Language.En, Loc.Instance.Current);
    }

    [AvaloniaFact]
    public void Back_AfterLanguageChange_ShowsLanguageStepInNewLanguage()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var welcome = env.Main.Welcome!;

        welcome.SelectSpanishCommand.Execute(null);
        welcome.ContinueLanguageCommand.Execute(null);
        Assert.Equal("Paso 2 de 3, Clientes", welcome.StepAnnouncement);

        welcome.BackCommand.Execute(null);

        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, welcome.Step);
        Assert.True(welcome.IsSpanish);
        Assert.Equal(Loc.Get("welcome.language.title"), welcome.Title);
        Assert.Equal("Paso 1 de 3, Idioma", welcome.StepAnnouncement);
        Assert.Equal("es", env.Session.UiPreferences.Load().LanguageCode);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void ClosingWindowMidFlow_DoesNotCompleteWelcome()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var window = new MainWindow(env.Main) { Width = 960, Height = 600 };
        window.Show();
        Screenshots.Settle();
        var welcome = env.Main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);
        welcome.BackCommand.Execute(null);
        welcome.ContinueLanguageCommand.Execute(null);
        Screenshots.Settle();

        window.Close();
        Screenshots.Settle();

        Assert.False(window.IsVisible);
        Assert.NotEqual(true, env.Session.UiPreferences.Load().WelcomeCompleted);
        Assert.False(env.Session.UiPreferences.Load().IsWelcomeDone);
        // The next launch shows the welcome again.
        var again = new MainViewModel(env.Session, autoTick: false);
        Assert.True(again.IsWelcomeActive);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, again.Welcome!.Step);
    }

    [AvaloniaFact]
    public void CompleteWelcome_RunsOnce_ForDoubleFinishAndFinishThenSkip()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var finished = 0;
        var welcome = new WelcomeViewModel(env.Session, env.Main.Clients, env.Main.Servers, env.Main.Settings, () => finished++);
        welcome.FinishCommand.Execute(null);
        welcome.FinishCommand.Execute(null);
        welcome.SkipCommand.Execute(null);
        Assert.Equal(1, finished);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);

        var other = 0;
        var second = new WelcomeViewModel(env.Session, env.Main.Clients, env.Main.Servers, env.Main.Settings, () => other++);
        second.SkipCommand.Execute(null);
        second.FinishCommand.Execute(null);
        Assert.Equal(1, other);
    }

    private static (MainWindow Window, WelcomeView View) ShowWelcome(MainViewModel main)
    {
        var window = new MainWindow(main) { Width = 960, Height = 600 };
        window.Show();
        Screenshots.Settle();
        return (window, window.GetVisualDescendants().OfType<WelcomeView>().Single());
    }

    private static void Press(Window window, Key key, PhysicalKey physical, string? symbol = null)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
        Screenshots.Settle();
    }

    [AvaloniaFact]
    public void AddServerForm_InWelcome_FocusesName_EscOnlyClosesUntouchedForm()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (main, welcome) = WelcomeOnServersStep(env);
        var (window, view) = ShowWelcome(main);

        // Clean form: focus starts in Name, Esc closes it and focus returns to the step.
        welcome.AddServerCommand.Execute(null);
        Screenshots.Settle();
        var name = Assert.IsType<TextBox>(window.FocusManager!.GetFocusedElement());
        Assert.Equal("NameBox", name.Name);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(welcome.IsServerFormOpen);
        Assert.True(main.IsWelcomeActive);
        Assert.Same(view.FindControl<Button>("FinishButton"), window.FocusManager!.GetFocusedElement());

        // Dirty form: Esc is ignored and the typed values stay; Cancel is the explicit way out.
        welcome.AddServerCommand.Execute(null);
        Screenshots.Settle();
        welcome.ServerForm!.Name = "Demo Realm";
        welcome.ServerForm.Address = "logon.example.com";
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.True(welcome.IsServerFormOpen);
        Assert.Equal("Demo Realm", welcome.ServerForm!.Name);
        Assert.Empty(welcome.ServerItems);

        welcome.ServerForm.CancelCommand.Execute(null);
        Screenshots.Settle();
        Assert.False(welcome.IsServerFormOpen);
        Assert.Same(view.FindControl<Button>("FinishButton"), window.FocusManager!.GetFocusedElement());
        window.Close();
    }

    [AvaloniaFact]
    public void HeldEnter_ActsOncePerKeyPress()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (window, _) = ShowWelcome(env.Main);
        var welcome = env.Main.Welcome!;

        // Auto-repeat: two key-downs without a key-up advance one step only.
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Screenshots.Settle();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Screenshots.Settle();
        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);
        Assert.True(env.Main.IsWelcomeActive);

        // After the key-up, the next press acts again.
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Press(window, Key.Enter, PhysicalKey.Enter, "\r");
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);
        Assert.True(env.Main.IsWelcomeActive);
        window.Close();
    }

    [AvaloniaFact]
    public void FinishOrSkip_WhenPreferencesCannotBeSaved_ShowsNoticeAndStaysRetryable()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var welcome = env.Main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);
        welcome.ContinueClientsCommand.Execute(null);

        // A directory where ui-preferences.json should be makes the save fail with an I/O error.
        var prefsPath = Path.Combine(env.Config, "ui-preferences.json");
        File.Delete(prefsPath);
        Directory.CreateDirectory(prefsPath);

        welcome.FinishCommand.Execute(null);
        Assert.True(env.Main.IsWelcomeActive);
        Assert.True(welcome.HasSaveError);
        Assert.Equal("Your choices could not be saved. Check that the settings folder can be written to, then try again.", welcome.SaveErrorText);
        welcome.SkipCommand.Execute(null);
        Assert.True(env.Main.IsWelcomeActive);
        Assert.True(welcome.HasSaveError);

        // Fixed: the retry completes the welcome.
        Directory.Delete(prefsPath);
        welcome.FinishCommand.Execute(null);
        Assert.False(welcome.HasSaveError);
        Assert.False(env.Main.IsWelcomeActive);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);
    }

    [AvaloniaFact]
    public void Enter_OnLanguageCard_PicksItAndContinues()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (window, view) = ShowWelcome(env.Main);
        var welcome = env.Main.Welcome!;
        Assert.Same(view.FindControl<Button>("EnglishCard"), window.FocusManager!.GetFocusedElement());

        Press(window, Key.Enter, PhysicalKey.Enter, "\r");

        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);
        Assert.True(welcome.IsEnglish);
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_OnClientCheckbox_ContinuesWithoutToggling_SpaceToggles()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var (main, welcome) = WelcomeOnServersStep(env);
        welcome.BackCommand.Execute(null);
        var (window, _) = ShowWelcome(main);
        var client = Assert.Single(main.Clients.Items);
        var box = Assert.IsType<CheckBox>(window.FocusManager!.GetFocusedElement());
        Assert.True(client.IsInUse);

        Press(window, Key.Space, PhysicalKey.Space, " ");
        Assert.False(client.IsInUse);
        Assert.False(box.IsChecked);
        Press(window, Key.Space, PhysicalKey.Space, " ");
        Assert.True(client.IsInUse);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);

        Press(window, Key.Enter, PhysicalKey.Enter, "\r");
        Assert.True(client.IsInUse);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);
        window.Close();
    }

    [AvaloniaFact]
    public void View_BackButton_HiddenOnLanguageStep_EscGoesBack_AndFocusesSelectedCard()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        var window = new MainWindow(env.Main) { Width = 960, Height = 600 };
        window.Show();
        Screenshots.Settle();
        var view = window.GetVisualDescendants().OfType<WelcomeView>().Single();
        var back = view.FindControl<Button>("BackButton")!;
        var welcome = env.Main.Welcome!;

        // Step 1: Back is not shown (so it is not focusable) and Esc does nothing.
        Assert.False(back.IsEffectivelyVisible);
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, welcome.Step);
        Assert.True(env.Main.IsWelcomeActive);

        welcome.ContinueLanguageCommand.Execute(null);
        Screenshots.Settle();
        Assert.True(back.IsEffectivelyVisible);
        Assert.Equal("Back", AutomationProperties.GetName(back));
        Assert.Equal("Step 2 of 3, Clients", AutomationProperties.GetName(view.FindControl<ItemsControl>("ProgressStrip")!));

        // Esc = Back on steps 2 and 3; focus goes to the selected language card.
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Screenshots.Settle();
        Assert.Equal(WelcomeViewModel.WelcomeStep.Language, welcome.Step);
        Assert.Same(view.FindControl<Button>("EnglishCard"), window.FocusManager!.GetFocusedElement());

        // Alt+Left also goes back.
        welcome.ContinueLanguageCommand.Execute(null);
        welcome.ContinueClientsCommand.Execute(null);
        Screenshots.Settle();
        Assert.Equal("Servers", AutomationProperties.GetName(view.FindControl<ItemsControl>("ServerList")!));
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left, KeyModifiers = KeyModifiers.Alt });
        Assert.Equal(WelcomeViewModel.WelcomeStep.Clients, welcome.Step);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SkipDuringClientsScan_DoesNotStartSecondScan()
    {
        using var temp = new TempDirectory();
        var scanRoot = temp.Combine("scan");
        var game = Path.Combine(scanRoot, "game");
        Directory.CreateDirectory(Path.Combine(game, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(game, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        using var env = new Env(additionalRoots: [scanRoot]);
        var welcome = env.Main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);
        await WaitUntil(() => welcome.Step == WelcomeViewModel.WelcomeStep.Clients && welcome.IsScanningClients);

        // Skip while the welcome-driven scan is still marked running. FinishWelcome must not
        // call StartFirstScanIfNeeded (which would clear then restart IsScanning).
        Assert.True(env.Main.Clients.IsScanning);
        env.Main.Welcome!.SkipCommand.Execute(null);
        Assert.False(env.Main.IsWelcomeActive);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);
        // Same in-flight scan is still the one running (no reset via a second StartFirstScanIfNeeded).
        Assert.True(env.Main.Clients.IsScanning);

        await WaitUntil(() => !env.Main.Clients.IsScanning);
        Assert.True(env.Session.UiPreferences.Load().FirstScanCompleted);
    }
}
