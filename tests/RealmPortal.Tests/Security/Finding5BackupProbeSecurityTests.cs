using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding5BackupProbeSecurityTests
{
    [Fact]
    public void SetStorageSettings_LeavesUniqueProbeFilesOnly()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(backups);

        var service = new PortalService(config, dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch(), probe: new NeverRunningGameProbe(), getVariable: _ => backups);
        Assert.True(service.SetStorageSettings(new StorageSettings(backups)).Succeeded);
        Assert.True(service.SetStorageSettings(new StorageSettings(backups)).Succeeded);
        Assert.DoesNotContain(Directory.GetFiles(backups).Select(Path.GetFileName),
            name => name!.StartsWith(".realm-portal-probe-", StringComparison.Ordinal));
    }

    [LinuxFact]
    public void SetStorageSettings_RejectsBackupFolderUnderSymlinkAncestor()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var real = dir.Combine("real");
        var link = dir.Combine("link");
        Directory.CreateDirectory(Path.Combine(real, "backups"));
        File.CreateSymbolicLink(link, real);

        var service = new PortalService(config, dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch(), probe: new NeverRunningGameProbe(), getVariable: TestBackupDir.Variables(dir));
        var result = service.SetStorageSettings(new StorageSettings(Path.Combine(link, "backups")));
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.PathNotFound, result.Error!.Code);
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
