using System.Text.Json;
using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding4StorageSettingsSecurityTests
{
    [Fact]
    public void SetStorageSettings_RequiresAbsolutePath()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        Directory.CreateDirectory(config);
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(backups);

        var service = new PortalService(
            config,
            dir.Combine("projection"),
            new RealmPortal.Core.Clients.PeClientVersionReader(),
            new NoLaunch(),
            getVariable: _ => backups);

        var result = service.SetStorageSettings(new StorageSettings("relative/backups"));
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.PathNotFound, result.Error!.Code);
    }

    [Fact]
    public void Load_RejectsUnsupportedVersion()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        Directory.CreateDirectory(config);
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(backups);
        var service = new PortalService(
            config,
            dir.Combine("projection"),
            new RealmPortal.Core.Clients.PeClientVersionReader(),
            new NoLaunch(),
            getVariable: _ => backups);

        var path = Path.Combine(config, "storage-settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = 99, backup_folder = backups }) + "\n");

        var settings = service.GetStorageSettings();
        Assert.False(settings.Succeeded);
    }

    private sealed class NoLaunch : RealmPortal.Core.Launch.IGameLaunchService
    {
        public void Launch(RealmPortal.Core.Launch.GameLaunchSpec spec) { }
    }
}
