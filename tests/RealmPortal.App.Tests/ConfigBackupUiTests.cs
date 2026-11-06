using Avalonia.Controls;
using Avalonia.VisualTree;
using RealmPortal.App.Views;
using Avalonia.Headless.XUnit;
using RealmPortal.App.Controls;
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
    public void Restore_IsDisabledWhenCurrentIsValid_WithReason()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        Assert.All(main.Settings.ConfigBackups, row =>
        {
            Assert.False(row.CanRestore);
            Assert.True(row.ShowRestoreDisabledReason);
            Assert.Contains("valid", row.RestoreDisabledReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ServerStatus.Ready, row.CurrentBadgeStatus);
        });
    }

    [AvaloniaFact]
    public void Restore_IsDisabledWhenBackupMissing_WithReason()
    {
        using var env = new Env();
        env.Session.UiPreferences.Save(new UiPreferences("en", [], FirstScanCompleted: true, WelcomeCompleted: true));
        // Damage servers.json with no prior .bak (fresh config has no backup until a successful write).
        var serversPath = Path.Combine(env.Config, "servers.json");
        File.WriteAllText(serversPath, "{ not json");
        if (File.Exists(serversPath + ".bak")) File.Delete(serversPath + ".bak");
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        var row = Assert.Single(main.Settings.ConfigBackups, r => r.Kind == ConfigFileKind.Servers);
        Assert.Equal(ConfigFileState.Damaged, row.Current);
        Assert.Equal(ConfigFileState.Missing, row.Backup);
        Assert.False(row.CanRestore);
        Assert.Contains("no backup", row.RestoreDisabledReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ServerStatus.NoResponse, row.CurrentBadgeStatus);
        Assert.Equal(ServerStatus.Inactive, row.BackupBadgeStatus);
    }

    [AvaloniaFact]
    public void RowAccessibleName_IncludesLongLocalizedDateAndState()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.Service.AddServer(new NewServer("Second", "play.example.net", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        Loc.Instance.SetLanguage(Language.En);
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        var row = Assert.Single(main.Settings.ConfigBackups, r => r.Kind == ConfigFileKind.Servers);
        Assert.NotNull(row.BackupModifiedAt);
        var expectedDate = ConfigBackupItemViewModel.FormatBackupDate(row.BackupModifiedAt.Value);
        Assert.Matches(@"^[A-Z][a-z]{2} \d{1,2}, \d{4}, \d{2}:\d{2}$", expectedDate);
        Assert.Contains(expectedDate, row.StatusAccessibleName, StringComparison.Ordinal);
        Assert.Contains(expectedDate, row.DetailText, StringComparison.Ordinal);
        Assert.Equal("Valid", row.BackupStateText);
        Assert.Equal("Valid", row.CurrentStateText);

        Loc.Instance.SetLanguage(Language.Es);
        row.RefreshText();
        var esDate = ConfigBackupItemViewModel.FormatBackupDate(row.BackupModifiedAt.Value);
        Assert.Matches(@"^\d{1,2} [a-z]{3} \d{4}, \d{2}:\d{2}$", esDate);
        Assert.Contains(esDate, row.StatusAccessibleName, StringComparison.Ordinal);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void StateBadges_AgreeInGenderForSpanishActualVsCopia()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.Service.AddServer(new NewServer("Second", "play.example.net", ClientId: cid));
        var serversPath = Path.Combine(env.Config, "servers.json");
        File.WriteAllText(serversPath, "{ not json");
        env.Session.UiPreferences.Save(new UiPreferences("es", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        Loc.Instance.SetLanguage(Language.Es);
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        var damaged = Assert.Single(main.Settings.ConfigBackups, r => r.Kind == ConfigFileKind.Servers);
        Assert.Equal("Dañado", damaged.CurrentStateText); // Actual = masculine
        Assert.Equal("Válida", damaged.BackupStateText); // Copia = feminine
        var clients = Assert.Single(main.Settings.ConfigBackups, r => r.Kind == ConfigFileKind.Clients);
        Assert.Equal("Válido", clients.CurrentStateText);
        Assert.Equal("Falta", clients.BackupStateText);
        Loc.Instance.SetLanguage(Language.En);
        damaged.RefreshText();
        Assert.Equal("Damaged", damaged.CurrentStateText);
        Assert.Equal("Valid", damaged.BackupStateText);
    }

    [AvaloniaFact]
    public void ConfirmRestoreDialog_BodyUsesDateAndBeforeRestoreFile_NoBakJargon()
    {
        Loc.Instance.SetLanguage(Language.En);
        var withDate = ConfirmDialogViewModel.RestoreConfigBackup(
            "servers.json", "servers.json.before-restore", "Nov 6, 2026, 10:04");
        Assert.Equal("Restore this configuration file?", withDate.Title);
        Assert.Contains("servers.json is damaged", withDate.Body, StringComparison.Ordinal);
        Assert.Contains("Nov 6, 2026, 10:04", withDate.Body, StringComparison.Ordinal);
        Assert.Contains("servers.json.before-restore", withDate.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(".bak", withDate.Body, StringComparison.Ordinal);
        Assert.Equal("Restore backup", withDate.ConfirmText);

        var noDate = ConfirmDialogViewModel.RestoreConfigBackup(
            "clients.json", "clients.json.before-restore");
        Assert.Contains("clients.json is damaged", noDate.Body, StringComparison.Ordinal);
        Assert.Contains("clients.json.before-restore", noDate.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("(", noDate.Body, StringComparison.Ordinal);

        Loc.Instance.SetLanguage(Language.Es);
        var es = ConfirmDialogViewModel.RestoreConfigBackup(
            "servers.json", "servers.json.before-restore", "6 nov 2026, 10:04");
        Assert.Contains("está dañado", es.Body, StringComparison.Ordinal);
        Assert.Contains("6 nov 2026, 10:04", es.Body, StringComparison.Ordinal);
        Assert.Contains("servers.json.before-restore", es.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(".bak", es.Body, StringComparison.Ordinal);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public async Task RestoreButton_HelpTextMatchesDisabledReason_AndCancelHasFocusRing()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        var row = Assert.Single(main.Settings.ConfigBackups, r => r.Kind == ConfigFileKind.Servers);
        Assert.False(row.CanRestore);
        Assert.False(string.IsNullOrWhiteSpace(row.RestoreDisabledReason));

        var window = new MainWindow(main) { Width = 960, Height = 600 };
        window.Show();
        main.SectionIndex = 2;
        Screenshots.Settle();
        var settings = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var button = settings.GetVisualDescendants().OfType<Button>()
            .First(b => b.Name == "RestoreConfigButton" && !b.IsEnabled);
        Assert.Equal(row.RestoreDisabledReason, Avalonia.Automation.AutomationProperties.GetHelpText(button));

        // Open confirm dialog and assert Cancel holds keyboard focus (focus-visible ring).
        var answer = main.ConfirmAsync(ConfirmDialogViewModel.RestoreConfigBackup(
            "servers.json", "servers.json.before-restore", "Nov 6, 2026, 10:04"));
        for (var i = 0; i < 20; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Screenshots.Settle();
        var dialog = window.GetVisualDescendants().OfType<ConfirmDialogView>().Single();
        var cancel = dialog.FindControl<Button>("CancelButton")!;
        Assert.True(cancel.IsKeyboardFocusWithin || cancel.IsFocused);
        Screenshots.Save(window, 1.0, "config-restore-dialog-960-en-1.0x.png");
        Screenshots.Save(window, 2.0, "config-restore-dialog-960-en-2.0x.png");
        cancel.Command!.Execute(null);
        await answer;
        window.Close();
    }

    [AvaloniaFact]
    public void PreferencesRow_ExposesBackupModifiedAtLikeServers()
    {
        using var env = new Env();
        // Two saves so ui-preferences.json.bak exists with an mtime.
        env.Session.UiPreferences.Save(new UiPreferences("en", [], FirstScanCompleted: true, WelcomeCompleted: true));
        env.Session.UiPreferences.Save(new UiPreferences("es", [], FirstScanCompleted: true, WelcomeCompleted: true));
        Assert.NotNull(env.Session.UiPreferences.BackupModifiedAt);
        Loc.Instance.SetLanguage(Language.En);
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Settings.ReloadConfigBackups();
        var prefs = Assert.Single(main.Settings.ConfigBackups, r => r.IsPreferences);
        Assert.NotNull(prefs.BackupModifiedAt);
        var date = ConfigBackupItemViewModel.FormatBackupDate(prefs.BackupModifiedAt.Value);
        Assert.Contains(date, prefs.DetailText, StringComparison.Ordinal);
        Assert.Contains(date, prefs.StatusAccessibleName, StringComparison.Ordinal);
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
