using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

/// <summary>QA-11b: permission errors must not print a full stack trace on stderr.</summary>
public class Bug4ReadOnlyProjectionCliTests
{
    [Fact]
    public void Switch_WithReadOnlyProjectionDir_PrintsShortError_NotStackTrace()
    {
        // A privileged process (root) ignores directory mode bits, so clearing write bits does not make the projection unwritable.
        if (!OperatingSystem.IsLinux() || SkipsReadOnlyDirectoryAssertion(OperatingSystem.IsLinux(), Environment.IsPrivilegedProcess))
            return;

        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var app = new CliApp(stdout, stderr, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir), probe: new NeverRunningGameProbe());
        Assert.Equal(0, app.Run("add", new[] { "Wotlk", "wotlk.example.org", "--config-dir", config, "--projection-dir", projection }));

        var mode = File.GetUnixFileMode(projection);
        File.SetUnixFileMode(projection, mode & ~UnixFileMode.UserWrite & ~UnixFileMode.GroupWrite & ~UnixFileMode.OtherWrite);

        try
        {
            var rc = app.Run("switch", new[] { "Wotlk", "--config-dir", config, "--projection-dir", projection });
            Assert.Equal(2, rc);
            var err = stderr.ToString();
            Assert.Contains("error:", err);
            Assert.DoesNotContain(" at RealmPortal.", err);
            Assert.DoesNotContain("System.", err);
        }
        finally
        {
            File.SetUnixFileMode(projection, mode);
        }
    }

    [Fact]
    public void ReadOnlyProjectionAssertion_SkipsWhenPrivilegedProcessIgnoresModeBits()
    {
        Assert.True(SkipsReadOnlyDirectoryAssertion(isLinux: true, isPrivilegedProcess: true));
        Assert.False(SkipsReadOnlyDirectoryAssertion(isLinux: true, isPrivilegedProcess: false));
        Assert.True(SkipsReadOnlyDirectoryAssertion(isLinux: false, isPrivilegedProcess: false));
        Assert.True(SkipsReadOnlyDirectoryAssertion(isLinux: false, isPrivilegedProcess: true));
    }

    // Same guard as ActiveServerManagerTests: directory mode bits are not enforced for a privileged process.
    internal static bool SkipsReadOnlyDirectoryAssertion(bool isLinux, bool isPrivilegedProcess) =>
        !isLinux || isPrivilegedProcess;

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
