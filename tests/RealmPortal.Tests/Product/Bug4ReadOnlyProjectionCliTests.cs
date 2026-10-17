using System.Runtime.InteropServices;
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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var app = new CliApp(stdout, stderr, new PeClientVersionReader(), new FakeLauncher(), _ => null);
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

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
