using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;

namespace RealmPortal.Tests.Security;

public class Finding5BackupProbeSecurityTests
{
    [Fact]
    public void SetStorageSettings_LeavesUniqueProbeFilesOnly()
    {
        using var dir = new Support.TempDirectory();
        var config = dir.Combine("config");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(backups);

        var service = new PortalService(config, dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch(), getVariable: _ => backups);
        Assert.True(service.SetStorageSettings(new StorageSettings(backups)).Succeeded);
        Assert.True(service.SetStorageSettings(new StorageSettings(backups)).Succeeded);
        Assert.DoesNotContain(".realm-portal-write-test", Directory.GetFiles(backups).Select(Path.GetFileName));
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
