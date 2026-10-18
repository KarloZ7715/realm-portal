using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;

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
        File.WriteAllBytes(path, new byte[65 * 1024]);

        var service = new PortalService(config, dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch());
        var result = service.ListClients();
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void SetStorageSettings_WritesConfigWithUserOnlyModeOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new Support.TempDirectory();
        var config = dir.Combine("config");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(backups);

        var service = new PortalService(config, dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch(), getVariable: _ => backups);
        Assert.True(service.SetStorageSettings(new StorageSettings(backups)).Succeeded);

        var mode = File.GetUnixFileMode(Path.Combine(config, "storage-settings.json"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
