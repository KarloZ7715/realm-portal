using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Service;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

public class Bug11CliMessagePolishTests
{
    [Fact]
    public void UnknownGlobalOptionBeforeCommand_PrintsUnknownOptionAndUsage()
    {
        using var dir = new TempDirectory();
        var stderr = new StringWriter();
        Assert.Equal(1, Program.Run(["--verbos", "list"], new StringWriter(), stderr, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir)));
        Assert.Contains("error: unknown option --verbos", stderr.ToString());
        Assert.Contains("Usage:", stderr.ToString());
    }

    [Fact]
    public void Add_WithInvalidBuild_StripsParameterSuffixFromMessage()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Combine("config"));
        Directory.CreateDirectory(dir.Combine("projection"));
        var stderr = new StringWriter();
        var app = new CliApp(new StringWriter(), stderr, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(dir));
        Assert.Equal(1, app.Run("add", ["Bad", "a.example.org", "--build", "0", "--config-dir", dir.Combine("config"), "--projection-dir", dir.Combine("projection")]));
        var err = stderr.ToString();
        Assert.Contains("positive integer", err, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Parameter '", err);
    }

    [Fact]
    public void Describe_PermissionDeniedVerbose_ReplacesAtomicTempWithLogicalFileName()
    {
        var diagnostic = "Access to the path '/tmp/proj/.activo.json.deadbeefdeadbeefdeadbeefdeadbeef.tmp' is denied.";
        var message = ServiceCli.Describe(
            new ServiceError(ServiceErrorCode.PermissionDenied, Diagnostic: diagnostic),
            verbose: true);
        Assert.Contains("activo.json", message);
        Assert.DoesNotContain(".tmp", message);
        Assert.DoesNotContain("deadbeef", message);
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
