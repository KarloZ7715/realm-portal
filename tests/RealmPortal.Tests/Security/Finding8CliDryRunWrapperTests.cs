using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding8CliDryRunWrapperTests
{
    [Fact]
    public void Launch_DryRunWithWrapper_DoesNotPersistLauncherSettings()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(client);
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        var o = new StringWriter();
        var e = new StringWriter();
        var launcher = new FakeLauncher();
        string[] common = ["--config-dir", config, "--projection-dir", projection, "--client-dir", client];

        Assert.Equal(0, Program.Run(["add", "Open", "open.example.org", .. common], o, e, new PeClientVersionReader(), launcher, _ => null));
        Assert.Equal(0, Program.Run(["switch", "Open", .. common], o, e, new PeClientVersionReader(), launcher, _ => null));
        Assert.Equal(0, Program.Run(["launch", "--dry-run", "--wrapper", "wine", .. common], o, e, new PeClientVersionReader(), launcher, _ => null));

        Assert.False(File.Exists(Path.Combine(config, "launcher-settings.json")));
        Assert.Empty(launcher.Calls);
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public List<(string Exe, string WorkDir, string? Wrapper)> Calls { get; } = [];

        public void Launch(string executablePath, string workingDirectory, string? wrapper) =>
            Calls.Add((executablePath, workingDirectory, wrapper));
    }
}
