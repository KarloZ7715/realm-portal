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
    public void ToSpec_AddsTheClientsExecutableAndFolder()
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

    [Fact]
    public void ServiceStep_HasNoAccountStep()
    {
        Assert.Equal(["Plan", "WriteFiles", "Activate", "Launch"], Enum.GetNames<ServiceStep>());
    }

    [Fact]
    public void StableCodesTheUiMapsExist()
    {
        string[] names = ["UndoConflict", "ProcessCheckFailed", "UnsupportedClient", "NothingToUndo", "GameRunning", "InvalidRealmlist"];

        Assert.All(names, n => Assert.True(Enum.TryParse<ServiceErrorCode>(n, out _), n));
    }

    [Fact]
    public void PlayLaunchFailure_UsesAppliedLaunchFailedConsistency()
    {
        var error = new ServiceError(ServiceErrorCode.LaunchFailed, Consistency: Consistency.AppliedLaunchFailed);
        var result = ServiceResult<SwitchOutcome>.Fail(error);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.LaunchFailed, result.Error!.Code);
        Assert.Equal(Consistency.AppliedLaunchFailed, result.Error.Consistency);
        Assert.NotEqual(Consistency.Unchanged, result.Error.Consistency);
        Assert.NotEqual(Consistency.RolledBack, result.Error.Consistency);
        Assert.NotEqual(Consistency.Inconsistent, result.Error.Consistency);
        Assert.NotEqual(Consistency.Pending, result.Error.Consistency);
    }

    [Fact]
    public void MissingFileAndDirectoryExceptions_MapToPathNotFound()
    {
        var file = ServiceErrors.FromException(new FileNotFoundException("missing file", "/games/wow/Wow.exe"));
        var fileWithoutPath = ServiceErrors.FromException(new FileNotFoundException("missing file"));
        var directory = ServiceErrors.FromException(new DirectoryNotFoundException("missing directory"));

        Assert.Equal(ServiceErrorCode.PathNotFound, file.Code);
        Assert.Equal("/games/wow/Wow.exe", file.Path);
        Assert.Equal(ServiceErrorCode.PathNotFound, fileWithoutPath.Code);
        Assert.Null(fileWithoutPath.Path);
        Assert.Equal(ServiceErrorCode.PathNotFound, directory.Code);
        Assert.Null(directory.Path);
    }

    [Fact]
    public void ServiceError_CarriesVersionAndConflictingFiles()
    {
        var conflict = new ServiceError(ServiceErrorCode.UndoConflict, ConflictingFiles: ["Data/enUS/realmlist.wtf"], Consistency: Consistency.Pending);
        var unsupported = new ServiceError(ServiceErrorCode.UnsupportedClient, ClientVersion: "6.2.4.20779");

        Assert.Equal(["Data/enUS/realmlist.wtf"], conflict.ConflictingFiles);
        Assert.Equal("6.2.4.20779", unsupported.ClientVersion);
    }
}
