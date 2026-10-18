using System.Text;
using RealmPortal.Cli;
using RealmPortal.Tests.Support;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;

namespace RealmPortal.Tests.Product;

public class Bug5VerboseGlobalFlagTests
{
    [Fact]
    public void Verbose_BeforeOrAfterCommand_IsAccepted()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var launcher = new FakeLauncher();
        var variables = TestBackupDir.Variables(dir);

        Assert.Equal(0, Program.Run(["add", "Wotlk", "wotlk.example.org", "--config-dir", config, "--projection-dir", projection],
            stdout, stderr, new PeClientVersionReader(), launcher, variables));

        Assert.Equal(0, Program.Run(["switch", "Wotlk", "--verbose", "--config-dir", config, "--projection-dir", projection],
            stdout, stderr, new PeClientVersionReader(), launcher, variables));
        File.WriteAllText(Path.Combine(config, "state.json"), "garbage", Encoding.UTF8);

        stderr = new StringWriter();
        var rcAfter = Program.Run(["switch", "Wotlk", "--verbose", "--config-dir", config, "--projection-dir", projection],
            stdout, stderr, new PeClientVersionReader(), launcher, variables);
        Assert.Equal(2, rcAfter);
        Assert.DoesNotContain("does not apply", stderr.ToString());

        stderr = new StringWriter();
        var rcBefore = Program.Run(["--verbose", "switch", "Wotlk", "--config-dir", config, "--projection-dir", projection],
            stdout, stderr, new PeClientVersionReader(), launcher, variables);
        Assert.Equal(2, rcBefore);
        Assert.Contains("state.json", stderr.ToString());
        Assert.Contains("is not valid", stderr.ToString());
        Assert.DoesNotContain("does not apply", stderr.ToString());
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
