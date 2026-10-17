using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.Tests.Product;

public class Bug8ServiceCliDescribeAllCodesTests
{
    [Fact]
    public void Add_WithInvalidBuild_PrintsReadableMessage_NotEnumName()
    {
        using var dir = new Support.TempDirectory();
        Directory.CreateDirectory(dir.Combine("config"));
        Directory.CreateDirectory(dir.Combine("projection"));
        var stderr = new StringWriter();
        var app = new CliApp(new StringWriter(), stderr, new PeClientVersionReader(), new FakeLauncher(), _ => null);
        Assert.Equal(2, app.Run("add", ["Bad", "a.example.org", "--build", "0", "--config-dir", dir.Combine("config"), "--projection-dir", dir.Combine("projection")]));
        Assert.Contains("positive number", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidServerInput", stderr.ToString());
    }

    [Fact]
    public void EveryServiceErrorCode_HasReadableCliMessage()
    {
        foreach (var code in Enum.GetValues<ServiceErrorCode>())
        {
            var error = SampleError(code);
            var message = ServiceCli.Describe(error, verbose: false);
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.DoesNotContain(code.ToString(), message);
        }
    }

    private static ServiceError SampleError(ServiceErrorCode code) =>
        code switch
        {
            ServiceErrorCode.ServerNameTaken => new(code, ServerName: "Dup"),
            ServiceErrorCode.IncompatibleClient => new(code, RequiredBuild: 12340, ClientBuild: 8606),
            ServiceErrorCode.InvalidRealmlist => new(code, Diagnostic: "bad realmlist"),
            ServiceErrorCode.StoreCorrupt => new(code, Path: "/tmp/config/state.json", Diagnostic: "detail"),
            ServiceErrorCode.InvalidServerInput => new(code, Field: "build", Diagnostic: "Client build must be a positive number."),
            ServiceErrorCode.PathNotFound => new(code, Path: "/tmp/missing"),
            ServiceErrorCode.UnsupportedClient => new(code, ClientVersion: "3.3.5.12340"),
            ServiceErrorCode.UndoConflict => new(code, ConflictingFiles: ["Data/enUS/realmlist.wtf"]),
            ServiceErrorCode.LaunchFailed => new(code, Diagnostic: "start failed"),
            ServiceErrorCode.ProcessCheckFailed => new(code, Diagnostic: "probe failed"),
            ServiceErrorCode.InconsistentState => new(code, Diagnostic: "rollback failed"),
            ServiceErrorCode.InvalidLauncherSettings => new(code, Diagnostic: "no {exe}"),
            ServiceErrorCode.DiskFull => new(code, Diagnostic: "no space"),
            ServiceErrorCode.FileSharingViolation => new(code, Diagnostic: "sharing"),
            _ => new(code),
        };

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
