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
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var shared = Path.Combine(client, "shared-realmlist.wtf");
        File.WriteAllText(shared, "set realmlist shared.example.org\r\n");
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, shared);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var plan = applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, ""));
        applier.Apply(plan);

        Assert.Null(File.ResolveLinkTarget(linkPath, returnFinalTarget: true));
        Assert.Equal("set realmlist logon-a.example.org\r\n", File.ReadAllText(linkPath));
        Assert.Equal("set realmlist shared.example.org\r\n", File.ReadAllText(shared));

        applier.Undo("install-1", client);

        Assert.Equal(Path.GetFullPath(shared), File.ResolveLinkTarget(linkPath, returnFinalTarget: true)?.FullName);
        Assert.Equal("set realmlist shared.example.org\r\n", File.ReadAllText(linkPath));
    }

    [Fact]
    public void Undo_PreservesRelativeSymlinkText_FromSidecar()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllText(Path.Combine(client, "shared-realmlist.wtf"), "set realmlist shared.example.org\r\n");
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, "../../shared-realmlist.wtf");

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        applier.Undo("install-1", client);

        Assert.Equal("../../shared-realmlist.wtf", new FileInfo(linkPath).LinkTarget);
    }

    [Fact]
    public void Undo_RejectsTamperedSymlinkSidecar_OutsideClient()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var shared = Path.Combine(client, "shared-realmlist.wtf");
        File.WriteAllText(shared, "set realmlist shared.example.org\r\n");
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, shared);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var sidecar = Path.Combine(backups, "install-1", result.BackupId!, "files", "0.symlink");
        File.WriteAllText(sidecar, "../../../etc/passwd");

        var ex = Assert.Throws<ClientFilesException>(() => applier.Undo("install-1", client));
        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Null(File.ResolveLinkTarget(linkPath, returnFinalTarget: false));
        Assert.Equal("set realmlist logon-a.example.org\r\n", File.ReadAllText(linkPath));
    }

    [Fact]
    public void Undo_FallsBackToRegularFile_WhenSymlinkVerifyFails()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllText(Path.Combine(client, "good.wtf"), "set realmlist shared.example.org\r\n");
        File.WriteAllText(Path.Combine(client, "bad.wtf"), "wrong content\r\n");
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, Path.Combine(client, "good.wtf"));

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var sidecar = Path.Combine(backups, "install-1", result.BackupId!, "files", "0.symlink");
        File.WriteAllText(sidecar, Path.Combine(client, "bad.wtf"));

        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        Assert.Equal("set realmlist shared.example.org\r\n", File.ReadAllText(linkPath));
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
