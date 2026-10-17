using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientFilesSymlinkTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    [Fact]
    public void Undo_RestoresSymlinkedRealmlist()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        var shared = temp.Combine("shared-realmlist.wtf");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllText(shared, "set realmlist shared.example.org\r\n");
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, shared);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var plan = applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, ""));
        applier.Apply(plan);

        Assert.Null(File.ResolveLinkTarget(linkPath));
        Assert.Equal("set realmlist logon-a.example.org\r\n", File.ReadAllText(linkPath));
        Assert.Equal("set realmlist shared.example.org\r\n", File.ReadAllText(shared));

        applier.Undo("install-1");

        Assert.Equal(shared, File.ResolveLinkTarget(linkPath, returnFinalTarget: true));
        Assert.Equal("set realmlist shared.example.org\r\n", File.ReadAllText(linkPath));
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
