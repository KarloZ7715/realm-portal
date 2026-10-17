using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientFilesPermissionTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    [Fact]
    public void Apply_AndUndo_PreserveReadOnlyUnixMode()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var path = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.WriteAllText(path, "set realmlist old.example.org\r\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var plan = applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, ""));
        var result = applier.Apply(plan);

        Assert.Contains(result.Warnings, w => w.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(path));

        applier.Undo("install-1", client);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(path));
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
