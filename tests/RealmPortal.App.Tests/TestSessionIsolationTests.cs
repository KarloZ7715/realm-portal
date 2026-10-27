using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Tests.Support;
using TempDirectory = RealmPortal.Tests.Support.TempDirectory;

namespace RealmPortal.App.Tests;

/// <summary>Guards for #159: App sessions never scan the real home or write the user backup folder.</summary>
public sealed class TestSessionIsolationTests
{
    [Fact]
    public void AssertNotUserBackupPath_RejectsTheDefaultUserFolder()
    {
        var userDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "realm-portal",
            "backups");
        Assert.Throws<InvalidOperationException>(() => TestSessions.AssertNotUserBackupPath(userDefault));
        Assert.Throws<InvalidOperationException>(() =>
            TestSessions.AssertNotUserBackupPath(Path.Combine(userDefault, "child")));
    }

    [Fact]
    public void Create_RejectsTheDefaultUserBackupFolder()
    {
        using var temp = new TempDirectory();
        var userDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "realm-portal",
            "backups");
        Assert.Throws<InvalidOperationException>(() =>
            TestSessions.Create(
                temp.Combine("config"),
                temp.Combine("projection"),
                userDefault,
                temp.Combine("home")));
    }

    [AvaloniaFact]
    public void DecoyHome_IsNotPickedUpByFirstScanOrMainViewModel()
    {
        using var temp = new TempDirectory();
        var decoyHome = temp.Combine("decoy-home");
        var decoyGame = Path.Combine(decoyHome, "Games", "World of Warcraft");
        Directory.CreateDirectory(Path.Combine(decoyGame, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(decoyGame, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        // Point HOME at the decoy and still use the factory: IncludeDefaultRoots is false, so the decoy stays unseen.
        var previousHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", decoyHome);
            using var env = new IsolatedAppSession(firstScanCompleted: true);
            Loc.Instance.SetLanguage(Language.En);
            var main = new MainViewModel(env.Session, autoTick: false);

            Assert.Empty(main.Clients.Items);
            Assert.False(main.Clients.IsScanning);
            Assert.True(env.Session.UiPreferences.Load().FirstScanCompleted);

            // Even an explicit Scan with the session options must not see the decoy.
            var scanned = env.Session.Service.Scan();
            Assert.True(scanned.Succeeded);
            Assert.Empty(scanned.Value!.AllClients);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            Loc.Instance.SetLanguage(Language.Es);
        }
    }

    [AvaloniaFact]
    public async Task IsolatedScan_AdditionalRoots_StillFindsSyntheticClients()
    {
        using var temp = new TempDirectory();
        var scanRoot = temp.Combine("scan");
        var game = Path.Combine(scanRoot, "game");
        Directory.CreateDirectory(Path.Combine(game, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(game, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        using var env = new IsolatedAppSession(
            firstScanCompleted: false,
            additionalScanRoots: [scanRoot]);
        Loc.Instance.SetLanguage(Language.En);
        try
        {
            var main = new MainViewModel(env.Session, autoTick: false);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (main.Clients.IsScanning && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.False(main.Clients.IsScanning);
            Assert.Single(main.Clients.Items);
        }
        finally
        {
            Loc.Instance.SetLanguage(Language.Es);
        }
    }
}
