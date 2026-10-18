using System.Text.Json;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// A corrupt storage-settings.json, or a backup folder that is missing or not writable,
/// must not stop PortalService from starting. The default backup root is used until
/// SetStorageSettings saves a usable folder.
/// </summary>
public class StorageSettingsStartupTests
{
    [Fact]
    public void CorruptStorageSettings_ServiceStarts_AndSetStorageSettingsRepairsIt()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var defaults = dir.Combine("default-backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(defaults);
        var path = Path.Combine(config, "storage-settings.json");
        File.WriteAllText(path, "{ not json\n");

        var service = Create(config, dir.Combine("projection"), defaults);
        var status = service.GetStatus();
        Assert.True(status.Succeeded);
        Assert.Equal(defaults, status.Value!.BackupFolder);

        var broken = service.GetStorageSettings();
        Assert.False(broken.Succeeded);
        Assert.Equal(ServiceErrorCode.StoreCorrupt, broken.Error!.Code);

        var repaired = dir.Combine("repaired-backups");
        Directory.CreateDirectory(repaired);
        var set = service.SetStorageSettings(new StorageSettings(repaired));
        Assert.True(set.Succeeded);

        var settings = service.GetStorageSettings();
        Assert.True(settings.Succeeded);
        Assert.Equal(Path.GetFullPath(repaired), settings.Value!.BackupFolder);
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Assert.Equal(
                Path.GetFullPath(repaired),
                document.RootElement.GetProperty("backup_folder").GetString());
        }

        var restarted = Create(config, dir.Combine("projection"), defaults);
        var restartedStatus = restarted.GetStatus();
        Assert.True(restartedStatus.Succeeded);
        Assert.Equal(Path.GetFullPath(repaired), restartedStatus.Value!.BackupFolder);
        Assert.True(restarted.GetStorageSettings().Succeeded);
    }

    [Fact]
    public void MissingBackupFolder_ServiceStillStarts_AndReportsPathNotFound()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var defaults = dir.Combine("default-backups");
        var missing = dir.Combine("missing-backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(defaults);
        WriteStorage(config, missing);

        var service = Create(config, dir.Combine("projection"), defaults);
        var status = service.GetStatus();
        Assert.True(status.Succeeded);
        Assert.Equal(defaults, status.Value!.BackupFolder);

        var settings = service.GetStorageSettings();
        Assert.False(settings.Succeeded);
        Assert.Equal(ServiceErrorCode.PathNotFound, settings.Error!.Code);
        Assert.Equal(Path.GetFullPath(missing), settings.Error.Path);
    }

    [Fact]
    public void UnwritableBackupFolder_ServiceStillStarts()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var defaults = dir.Combine("default-backups");
        var locked = dir.Combine("locked-backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(defaults);
        Directory.CreateDirectory(locked);
        WriteStorage(config, locked);
        var mode = File.GetUnixFileMode(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var service = Create(config, dir.Combine("projection"), defaults);
            var status = service.GetStatus();
            Assert.True(status.Succeeded);
            Assert.Equal(defaults, status.Value!.BackupFolder);

            var settings = service.GetStorageSettings();
            Assert.False(settings.Succeeded);
            Assert.Equal(ServiceErrorCode.PermissionDenied, settings.Error!.Code);
        }
        finally
        {
            File.SetUnixFileMode(locked, mode);
        }
    }

    private static void WriteStorage(string config, string backupFolder)
    {
        var json = JsonSerializer.Serialize(new { version = 1, backup_folder = backupFolder }) + "\n";
        File.WriteAllText(Path.Combine(config, "storage-settings.json"), json);
    }

    private static PortalService Create(string config, string projection, string defaults) =>
        new(config, projection, new PeClientVersionReader(), new NoLaunch(), getVariable: _ => defaults);

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
