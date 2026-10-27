using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
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
    public void ServersStep_AddServer_FinishesAndOpensForm()
    {
        using var env = new Env(welcomeCompleted: false, firstScanCompleted: true);
        // Force welcome even though first scan is done.
        env.Session.UiPreferences.Save(new UiPreferences("en", [], FirstScanCompleted: true, WelcomeCompleted: false));
        var main = new MainViewModel(env.Session, autoTick: false);
        // IsWelcomeDone = false ?? true wait: WelcomeCompleted false means IsWelcomeDone = false (not null).
        Assert.True(main.IsWelcomeActive);
        var welcome = main.Welcome!;
        welcome.ContinueLanguageCommand.Execute(null);
        // Clients step may try to scan; first scan already completed so it reloads only.
        welcome.ContinueClientsCommand.Execute(null);
        Assert.Equal(WelcomeViewModel.WelcomeStep.Servers, welcome.Step);

        welcome.AddServerCommand.Execute(null);

        Assert.False(main.IsWelcomeActive);
        Assert.True(env.Session.UiPreferences.Load().WelcomeCompleted);
        Assert.IsType<AddServerViewModel>(main.Content);
    }
}
