using System.Text.Json;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

public class Bug7FailedSwitchRollsBackStateTests
{
    [LinuxFact]
    public void Switch_WithReadOnlyProjectionDir_LeavesStateAndActivoUnchanged()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var app = new CliApp(new StringWriter(), new StringWriter(), new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir), probe: new NeverRunningGameProbe());
        Assert.Equal(0, app.Run("add", ["Wotlk", "wotlk.example.org", "--config-dir", config, "--projection-dir", projection]));
        Assert.Equal(0, app.Run("add", ["Second", "second.example.org", "--config-dir", config, "--projection-dir", projection]));
        Assert.Equal(0, app.Run("switch", ["Wotlk", "--config-dir", config, "--projection-dir", projection]));
        Assert.Equal(0, app.Run("switch", ["Second", "--config-dir", config, "--projection-dir", projection]));

        var stateBefore = File.ReadAllText(Path.Combine(config, ActiveServerManager.StateFileName));
        var activoBefore = File.ReadAllText(ActiveProjection.FilePath(projection));
        using var beforeDoc = JsonDocument.Parse(stateBefore);
        var secondId = beforeDoc.RootElement.GetProperty("active_id").GetString();

        var mode = File.GetUnixFileMode(projection);
        File.SetUnixFileMode(projection, mode & ~UnixFileMode.UserWrite & ~UnixFileMode.GroupWrite & ~UnixFileMode.OtherWrite);
        try
        {
            Assert.Equal(2, app.Run("switch", ["Wotlk", "--config-dir", config, "--projection-dir", projection]));
            Assert.Equal(stateBefore, File.ReadAllText(Path.Combine(config, ActiveServerManager.StateFileName)));
            Assert.Equal(activoBefore, File.ReadAllText(ActiveProjection.FilePath(projection)));
            using var afterDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, ActiveServerManager.StateFileName)));
            Assert.Equal(secondId, afterDoc.RootElement.GetProperty("active_id").GetString());
        }
        finally
        {
            File.SetUnixFileMode(projection, mode);
        }
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
