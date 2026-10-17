using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Service;

namespace RealmPortal.Tests;

public class ServiceContractTests
{
    [Fact]
    public void EveryClientFilesErrorCode_HasACodeWithTheSameName()
    {
        // The service maps ClientFilesErrorCode by name (see docs/app-service.md); a new applier code must get a service code.
        var serviceNames = Enum.GetNames<ServiceErrorCode>().ToHashSet();

        var missing = Enum.GetNames<ClientFilesErrorCode>().Where(n => !serviceNames.Contains(n)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryClientVersionErrorKind_IsCoveredByAClientCode()
    {
        // ExecutableNotFound/NotAPortableExecutable/NoVersionResource/Unreadable <-> Client* codes (PathNotFound for a missing folder).
        Assert.Equal(4, Enum.GetValues<ClientVersionErrorKind>().Length);
        string[] codes = ["ClientExecutableNotFound", "ClientNotAPortableExecutable", "ClientNoVersionResource", "ClientUnreadable"];
        Assert.All(codes, c => Assert.True(Enum.TryParse<ServiceErrorCode>(c, out _)));
    }

    [Fact]
    public void ServiceResult_CarriesValueOrError()
    {
        var ok = ServiceResult<int>.Ok(5);
        var fail = ServiceResult<int>.Fail(new ServiceError(ServiceErrorCode.GameRunning));

        Assert.True(ok.Succeeded);
        Assert.Equal(5, ok.Value);
        Assert.Null(ok.Error);
        Assert.False(fail.Succeeded);
        Assert.Equal(ServiceErrorCode.GameRunning, fail.Error!.Code);
        Assert.Equal(Consistency.Unchanged, fail.Error.Consistency);
        Assert.Throws<ArgumentNullException>(() => ServiceResult<int>.Fail(null!));
    }

    [Fact]
    public void LauncherSettings_ToSpec_AddsTheClientsExecutableAndFolder()
    {
        var settings = new LauncherSettings(
            "wine {exe}",
            ["-windowed"],
            new Dictionary<string, string?> { ["WINEPREFIX"] = "/p", ["DISPLAY"] = null },
            ["gamemoderun"]);

        var launch = settings.ToSpec("/games/wow/Wow.exe", "/games/wow").Resolve();

        Assert.Equal("gamemoderun", launch.FileName);
        Assert.Equal(["wine", "/games/wow/Wow.exe", "-windowed"], launch.Arguments);
        Assert.Equal("/games/wow", launch.WorkingDirectory);
        Assert.Null(launch.Environment["DISPLAY"]);
    }

    [Fact]
    public void LauncherSettings_Default_StartsTheExecutableDirectly()
    {
        var launch = new LauncherSettings().ToSpec("/g/Wow.exe", "/g").Resolve();

        Assert.Equal("/g/Wow.exe", launch.FileName);
        Assert.Empty(launch.Arguments);
    }
}
