using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding7PortalConfigFileSecurityTests
{
    [Fact]
    public void ListClients_RejectsOversizedClientsJson()
    {
        using var dir = new Support.TempDirectory();
        var config = dir.Combine("config");
        Directory.CreateDirectory(config);
        var path = Path.Combine(config, ClientStore.FileName);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            stream.SetLength(16 * 1024 * 1024 + 1);

        var service = new PortalService(config, dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch(), probe: new NeverRunningGameProbe(), getVariable: Support.TestBackupDir.Variables(dir));
        var result = service.ListClients();
        Assert.False(result.Succeeded);
    }

    [NonWindowsFact]
    public void SetStorageSettings_WritesConfigWithUserOnlyModeOnUnix()
    {
        using var dir = new Support.TempDirectory();
        var config = dir.Combine("config");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(backups);

        var service = new PortalService(config, dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch(), probe: new NeverRunningGameProbe(), getVariable: _ => backups);
        Assert.True(service.SetStorageSettings(new StorageSettings(backups)).Succeeded);

        var mode = File.GetUnixFileMode(Path.Combine(config, "storage-settings.json"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
