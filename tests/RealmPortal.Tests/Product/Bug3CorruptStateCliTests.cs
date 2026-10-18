using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

/// <summary>QA-10m: corrupt state.json must not crash the CLI on switch.</summary>
public class Bug3CorruptStateCliTests
{
    [Fact]
    public void Switch_WithCorruptStateJson_Returns2_AndReportsStoreCorrupt()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var app = new CliApp(stdout, stderr, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir));

        Assert.Equal(0, app.Run("add", new[] { "Second", "second.example.org", "--config-dir", config, "--projection-dir", projection }));
        File.WriteAllText(Path.Combine(config, "state.json"), "garbage", Encoding.UTF8);

        var rc = app.Run("switch", new[] { "Second", "--config-dir", config, "--projection-dir", projection });
        Assert.Equal(2, rc);
        var err = stderr.ToString();
        Assert.Contains("error:", err);
        Assert.Contains("corrupt", err, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unhandled exception", err);
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
