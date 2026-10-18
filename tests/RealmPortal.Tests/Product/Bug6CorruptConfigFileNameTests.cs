using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

public class Bug6CorruptConfigFileNameTests
{
    [Fact]
    public void Switch_WithCorruptStateJson_NamesStateJsonInShortMessage()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var app = CreateApp(dir);
        Assert.Equal(0, app.Run("add", ["Second", "second.example.org", "--config-dir", config, "--projection-dir", projection]));
        File.WriteAllText(Path.Combine(config, "state.json"), "garbage", Encoding.UTF8);

        var stderr = new StringWriter();
        var app2 = new CliApp(new StringWriter(), stderr, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir));
        Assert.Equal(2, app2.Run("switch", ["Second", "--config-dir", config, "--projection-dir", projection]));
        Assert.Contains("state.json", stderr.ToString());
        Assert.DoesNotContain("Unhandled", stderr.ToString());
    }

    [Fact]
    public void List_WithCorruptServersJson_NamesServersJsonInShortMessage()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        File.WriteAllText(Path.Combine(config, "servers.json"), "not-json", Encoding.UTF8);

        var stderr = new StringWriter();
        var app = new CliApp(new StringWriter(), stderr, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir));
        Assert.Equal(2, app.Run("list", ["--config-dir", config, "--projection-dir", projection]));
        Assert.Contains("servers.json", stderr.ToString());
    }

    private static CliApp CreateApp(TempDirectory dir) =>
        new(new StringWriter(), new StringWriter(), new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir));

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
