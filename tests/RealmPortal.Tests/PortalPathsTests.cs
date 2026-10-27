using RealmPortal.Core;

namespace RealmPortal.Tests;

public class PortalPathsTests
{
    [Fact]
    public void Overrides_AreHonoured()
    {
        string? Env(string name) => name switch
        {
            PortalPaths.ConfigDirVariable => "/x/cfg",
            PortalPaths.ProjectionDirVariable => "/x/proj",
            PortalPaths.StateDirVariable => "/x/state",
            _ => null,
        };

        Assert.Equal("/x/cfg", PortalPaths.ConfigDirectory(Env));
        Assert.Equal("/x/proj", PortalPaths.ProjectionDirectory(Env));
        Assert.Equal("/x/state", PortalPaths.StateDirectory(Env));
        Assert.Equal(Path.Combine("/x/state", "logs", ExceptionLog.FileName), PortalPaths.CrashLogFile(Env));
    }

    [Fact]
    public void Defaults_EndWithTheDocumentedLocations()
    {
        var config = PortalPaths.ConfigDirectory(_ => null);
        var projection = PortalPaths.ProjectionDirectory(_ => "  ");

        Assert.Equal("realm-portal", Path.GetFileName(config));
        Assert.EndsWith(Path.Combine(".config", "wow-servidores"), projection);
    }

    [Fact]
    public void CrashLogFile_DefaultEndsWithDocumentedName()
    {
        var log = PortalPaths.CrashLogFile(_ => null);
        Assert.Equal(ExceptionLog.FileName, Path.GetFileName(log));
        Assert.Equal("logs", Path.GetFileName(Path.GetDirectoryName(log)!));
    }
}
