using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class CliTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        using var dir = new TempDirectory();
        var o = new StringWriter();
        var e = new StringWriter();
        var code = Program.Run(args, o, e, new PeClientVersionReader(), getVariable: TestBackupDir.Variables(dir));
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void ClientVersion_PrintsBuildAndVersion()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.Combine("Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        var (code, output, err) = Run("client-version", dir.Path);

        Assert.Equal(0, code);
        Assert.Equal("12340 (3.3.5.12340)", output.Trim());
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void ClientVersion_OnError_ReturnsTwoAndWritesToStderr()
    {
        using var dir = new TempDirectory();

        var (code, output, err) = Run("client-version", dir.Path);

        Assert.Equal(2, code);
        Assert.Equal(string.Empty, output);
        Assert.StartsWith("error: ", err);
    }

    [Fact]
    public void UnknownCommand_ReturnsOneAndPrintsUsage()
    {
        var (code, output, _) = Run("bogus");

        Assert.Equal(1, code);
        Assert.Contains("Usage:", output);
    }
}
