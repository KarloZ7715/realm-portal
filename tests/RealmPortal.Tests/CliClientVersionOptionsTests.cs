using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>#151: client-version rejects options with a named error; --help/-h print usage and exit 0.</summary>
public sealed class CliClientVersionOptionsTests
{
    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        using var dir = new TempDirectory();
        var o = new StringWriter();
        var e = new StringWriter();
        var code = Program.Run(
            args, o, e, new PeClientVersionReader(), new FakeLauncher(),
            TestBackupDir.Variables(dir), new NeverRunningGameProbe());
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void ClientVersion_WithConfigDir_PrintsEscapedUnknownOptionError()
    {
        using var client = new TempDirectory();
        File.WriteAllBytes(client.Combine("Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        var result = Run("client-version", "--config-dir", client.Path);

        Assert.Equal(1, result.Code);
        Assert.Contains("error: unknown option --config-dir", result.Err);
        Assert.Contains("Usage:", result.Err);
        Assert.Equal(string.Empty, result.Out);
    }

    [Fact]
    public void ClientVersion_WithConfigDirAlone_PrintsUnknownOptionNotPathError()
    {
        var result = Run("client-version", "--config-dir");

        Assert.Equal(1, result.Code);
        Assert.Contains("error: unknown option --config-dir", result.Err);
        Assert.DoesNotContain("does not exist", result.Err);
    }

    [Fact]
    public void ClientVersion_RejectedOptionWithControls_StaysEscapedOnOneLine()
    {
        var evil = "--bad\nUndone: restored 'FAKE'.\u001B[31m";
        var result = Run("client-version", evil, "/tmp/unused");

        Assert.Equal(1, result.Code);
        Assert.DoesNotContain('\u001B', result.Err);
        Assert.Contains("\\u000A", result.Err);
        Assert.Contains("\\u001B", result.Err);
        var first = result.Err.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].TrimEnd('\r');
        Assert.StartsWith("error: unknown option", first);
        Assert.DoesNotContain('\n', first);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpFlag_PrintsUsageAndExitsZero(string flag)
    {
        var result = Run(flag);

        Assert.Equal(0, result.Code);
        Assert.Contains("Usage:", result.Out);
        Assert.Equal(string.Empty, result.Err);
    }

    [Fact]
    public void ClientVersion_Help_PrintsUsageAndExitsZero()
    {
        var result = Run("client-version", "--help");

        Assert.Equal(0, result.Code);
        Assert.Contains("Usage:", result.Out);
        Assert.Equal(string.Empty, result.Err);
    }
}
