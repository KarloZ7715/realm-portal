using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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

public sealed class BackupsViewModelTests
{
    private sealed class FakeLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    private sealed class Probe : IGameProcessProbe
    {
        public bool Running { get; set; }
        public bool IsGameRunning(string clientRoot) => Running;
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env(bool running = false)
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Backups = _temp.Combine("backups");
            Client = _temp.Combine("client");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Directory.CreateDirectory(Path.Combine(Client, "WTF"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            File.WriteAllText(Realmlist, "set realmlist original.example\r\n");
            File.WriteAllText(ConfigFile, "SET realmName \"Original\"\r\n");
            Probe.Running = running;
            var getVariable = (string name) => name == ClientFilesApplier.BackupDirectoryVariable ? Backups : null;
            var service = new PortalService(Config, Projection, new PeClientVersionReader(), new FakeLaunch(), Probe, getVariable);
            Session = new PortalSession(service, new UiPreferencesStore(Config), getVariable);
            ClientId = Session.Service.AddClient(Client, "Test client").Value!.Id;
            Loc.Instance.SetLanguage(Language.En);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Backups { get; }
        public string Client { get; }
        public string Realmlist => Path.Combine(Client, "Data", "enUS", "realmlist.wtf");
        public string ConfigFile => Path.Combine(Client, "WTF", "Config.wtf");
        public string ClientId { get; }
        public Probe Probe { get; } = new();
        public PortalSession Session { get; }
        public BackupsViewModel CreateViewModel() => new(Session, () => { });
        public void Dispose() => _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task CreateBackup_CreatesThenDeduplicatesAnUnchangedSnapshot()
    {
        using var env = new Env();
        var vm = env.CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.CreateBackupCommand.ExecuteAsync(null);
        var first = Assert.Single(vm.Backups);
        Assert.False(first.Entry.WasDeduplicated);

        await vm.CreateBackupCommand.ExecuteAsync(null);

        Assert.Single(vm.Backups);
        Assert.Contains("already matches", vm.Notice, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task RestoreBackup_WithNoConflict_MarksSnapshotRestored()
    {
        using var env = new Env();
        var vm = env.CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CreateBackupCommand.ExecuteAsync(null);
        var backup = Assert.Single(vm.Backups);

        await backup.RestoreCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Backups.Single().Entry.UndoneAt);
        Assert.Contains("restored", vm.Notice, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task RestoreBackup_ConflictPromptsAndCancelLeavesClientFilesUnchanged()
    {
        using var env = new Env();
        var vm = env.CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CreateBackupCommand.ExecuteAsync(null);
        File.WriteAllText(env.Realmlist, "set realmlist edited.example\r\n");
        var prompted = 0;
        vm.ResolveRestoreConflictAsync = _ =>
        {
            prompted++;
            return Task.FromResult(ConfirmDialogChoice.Cancel);
        };

        await vm.Backups.Single().RestoreCommand.ExecuteAsync(null);

        Assert.Equal(1, prompted);
        Assert.Equal("set realmlist edited.example\r\n", File.ReadAllText(env.Realmlist));
    }

    [AvaloniaFact]
    public async Task RestoreBackup_ConflictRestoreAnywayCreatesSafetyBackup()
    {
        using var env = new Env();
        var vm = env.CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CreateBackupCommand.ExecuteAsync(null);
        var oldBackup = vm.Backups.Single().Entry;
        var server = env.Session.Service.AddServer(new NewServer("Current server", "current.example")).Value!;
        var switched = env.Session.Service.SwitchServer(new SwitchRequest(env.ClientId, server.Id));
        Assert.True(switched.Succeeded);
        vm.ResolveRestoreConflictAsync = _ => Task.FromResult(ConfirmDialogChoice.Confirm);

        await vm.Backups.Single(item => item.Entry.BackupId == oldBackup.BackupId).RestoreCommand.ExecuteAsync(null);

        Assert.Equal("set realmlist original.example\r\n", File.ReadAllText(env.Realmlist));
        Assert.Contains(env.Session.Service.ListBackups(env.ClientId).Value!, backup => backup.Label == "pre-restore");
        Assert.DoesNotContain(env.Session.Service.ListBackups(env.ClientId).Value!, backup => backup.IsPendingUndo);
    }

    [AvaloniaFact]
    public async Task DeleteBackup_ConfirmsThenRemovesSelectedSnapshot()
    {
        using var env = new Env();
        var vm = env.CreateViewModel();
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CreateBackupCommand.ExecuteAsync(null);
        var confirmed = false;
        vm.ConfirmDeleteBackupAsync = _ =>
        {
            confirmed = true;
            return Task.FromResult(true);
        };

        await vm.Backups.Single().DeleteCommand.ExecuteAsync(null);

        Assert.True(confirmed);
        Assert.Empty(vm.Backups);
    }

    [AvaloniaFact]
    public void RunningClient_DisablesBackupCreation()
    {
        using var env = new Env(running: true);
        var vm = env.CreateViewModel();

        Assert.False(vm.CreateBackupCommand.CanExecute(null));
        Assert.True(vm.IsGameRunning);
    }

    [AvaloniaFact]
    public async Task MainViewModel_PropagatesBackupStatusAndRefreshesLanguage()
    {
        using var env = new Env();
        var main = new MainViewModel(env.Session, autoTick: false);
        main.SectionIndex = 2;
        main.Settings.OpenBackupsManagerCommand.Execute(null);
        var vm = Assert.IsType<BackupsViewModel>(main.Content);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal(vm.StatusText, main.StatusText);
        Loc.Instance.SetLanguage(Language.Es);
        Assert.Contains("máx.", vm.CountText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(vm.StatusText, main.StatusText);

        File.WriteAllText(env.Realmlist, "set realmlist edited.example\r\n");
        var restoreTask = vm.Backups.Single().RestoreCommand.ExecuteAsync(null);
        for (var i = 0; i < 100 && main.Dialog is null; i++)
            await Task.Delay(10);
        Assert.NotNull(main.Dialog);
        Assert.False(main.Dialog!.HasSecondaryAction);
        main.Dialog.ConfirmCommand.Execute(null);
        await restoreTask;
        Assert.Equal("set realmlist original.example\r\n", File.ReadAllText(env.Realmlist));
    }
    [AvaloniaFact]
    public async Task BackButton_ShowsASingleChevronAndPlainText()
    {
        using var env = new Env();
        var main = new MainViewModel(env.Session, autoTick: false);
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        main.SectionIndex = 2;
        main.Settings.OpenBackupsManagerCommand.Execute(null);
        var vm = Assert.IsType<BackupsViewModel>(main.Content);
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CreateBackupCommand.ExecuteAsync(null);
        Screenshots.Settle();

        try
        {
            foreach (var (language, label) in new[] { (Language.En, "Back"), (Language.Es, "Volver") })
            {
                Loc.Instance.SetLanguage(language);
                Screenshots.Settle();
                var back = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BackButton");
                Assert.Single(back.GetVisualDescendants().OfType<Icon>());
                var texts = back.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Equal([label], texts);
                Assert.Equal(label, Avalonia.Automation.AutomationProperties.GetName(back));
                foreach (var scale in new[] { 1.0, 2.0 })
                    Screenshots.Save(window, scale, $"backups-{language.ToString().ToLowerInvariant()}-{scale:0.0}x.png");
                Screenshots.SetRenderScaling(window, 1.0);
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(Language.En);
            window.Close();
        }
    }
}
