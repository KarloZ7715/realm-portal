using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

public class UiPreferencesLoadTests
{
    [Fact]
    public void Save_CreatesPrivateConfigAndPreferencesOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var temp = new TempDirectory();
        var config = temp.Combine("config");
        new UiPreferencesStore(config).Save(new UiPreferences("en"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(config));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(config, "ui-preferences.json")));
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("This test does not launch the game.");
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("empty")]
    [InlineData("directory")]
    public void Load_BadFile_ReturnsDefaultsAndSaveReplacesCorruptJson(string kind)
    {
        using var temp = new TempDirectory();
        var config = temp.Combine("config");
        Directory.CreateDirectory(config);
        Seed(config, kind);
        var store = new UiPreferencesStore(config);

        var loaded = store.Load();
        Assert.Null(loaded.LanguageCode);
        Assert.Empty(loaded.InUseSet);

        if (kind == "directory")
        {
            return;
        }

        store.Save(new UiPreferences("es", ["client-1"]));
        var again = store.Load();
        Assert.Equal("es", again.LanguageCode);
        Assert.Equal(["client-1"], again.InUseSet.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Load_UnreadableFile_ReturnsDefaults()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        if (geteuid() == 0)
        {
            return;
        }

        using var temp = new TempDirectory();
        var config = temp.Combine("config");
        Directory.CreateDirectory(config);
        var path = Path.Combine(config, "ui-preferences.json");
        File.WriteAllText(path, """{"LanguageCode":"es","ClientsInUse":["secret"]}""");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var loaded = new UiPreferencesStore(config).Load();
            Assert.Null(loaded.LanguageCode);
            Assert.Empty(loaded.InUseSet);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Load_OversizedFile_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var config = temp.Combine("config");
        Directory.CreateDirectory(config);
        var path = Path.Combine(config, "ui-preferences.json");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            file.SetLength(RealmPortal.Core.IO.PrivateStorage.MaxJsonBytes + 1L);

        var loaded = new UiPreferencesStore(config).Load();

        Assert.Null(loaded.LanguageCode);
        Assert.Empty(loaded.InUseSet);
    }

    [AvaloniaTheory]
    [InlineData("corrupt")]
    [InlineData("empty")]
    [InlineData("directory")]
    public void PortalSessionAndMainViewModel_OverBadPreferences_DoNotThrow(string kind)
    {
        using var temp = new TempDirectory();
        var config = temp.Combine("config");
        var projection = temp.Combine("projection");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(backups);
        Seed(config, kind);

        var session = PortalSession.CreateForPaths(config, projection, backups, new NoLaunch());
        var main = new MainViewModel(session, () => Task.FromResult<string?>(null), autoTick: false);
        var loaded = session.UiPreferences.Load();

        Assert.NotNull(main.Clients);
        Assert.Null(loaded.LanguageCode);
        Assert.Empty(loaded.InUseSet);
    }

    private static void Seed(string config, string kind)
    {
        var path = Path.Combine(config, "ui-preferences.json");
        switch (kind)
        {
            case "corrupt":
                File.WriteAllText(path, "{");
                break;
            case "empty":
                File.WriteAllText(path, string.Empty);
                break;
            case "directory":
                Directory.CreateDirectory(path);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown preferences fixture.");
        }
    }

    [DllImport("libc")]
    private static extern uint geteuid();
}
