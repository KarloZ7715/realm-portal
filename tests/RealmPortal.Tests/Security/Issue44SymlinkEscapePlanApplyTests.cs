using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

/// <summary>
/// Issue #44: Plan/Apply must not follow managed-path symlinks that escape the client root
/// (directory link writes outside, file link content copied into backups, undo delete through links).
/// In-client links remain allowed. Unix only (creating links needs privilege on Windows).
/// </summary>
public sealed class Issue44SymlinkEscapePlanApplyTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private const string OldRealmlist = "set realmlist old.example.org\r\n";
    private const string Secret = "TOP-SECRET-from-outside\r\n";
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();
        public string Client { get; }
        public string Outside { get; }
        public string Backups { get; }
        public ClientFilesApplier Applier { get; }

        public Env()
        {
            Client = _temp.Combine("client");
            Outside = _temp.Combine("outside");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Client);
            Directory.CreateDirectory(Outside);
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunningGameProbe(), Backups);
        }

        public ClientFilesRequest Request(string realm = "") =>
            new("install-1", "profile-1", Client, V3, BlobA, realm);

        public string InClient(params string[] parts) => Path.Combine([Client, .. parts]);
        public string InOutside(params string[] parts) => Path.Combine([Outside, .. parts]);
        public void Dispose() => _temp.Dispose();
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                p => Path.GetRelativePath(root, p),
                p => File.Exists(p) ? ClientFilesIo.Sha256Hex(File.ReadAllBytes(p)) : "<dir>");

    [UnixFact]
    public void Plan_FileSymlinkRealmlistToOutside_RefusesAndDoesNotExfiltrateSecret()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data", "enUS"));
        File.WriteAllText(env.InOutside("secret.txt"), Secret);
        File.CreateSymbolicLink(env.InClient("Data", "enUS", "realmlist.wtf"), env.InOutside("secret.txt"));

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Plan(env.Request()));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(Secret, File.ReadAllText(env.InOutside("secret.txt")));
        Assert.False(Directory.Exists(env.Backups));
    }

    [UnixFact]
    public void Apply_DirectorySymlinkWtfToOutside_RefusesAndWritesNothingOutside()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data", "enUS"));
        File.WriteAllText(env.InClient("Data", "enUS", "realmlist.wtf"), OldRealmlist);
        Directory.CreateDirectory(env.InOutside("WTF"));
        File.WriteAllText(env.InOutside("WTF", "Config.wtf"), "SET realmName \"Old\"\r\n");
        Directory.CreateSymbolicLink(env.InClient("WTF"), env.InOutside("WTF"));
        var outsideBefore = Snapshot(env.Outside);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Plan(env.Request("Realm")));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(outsideBefore, Snapshot(env.Outside));
    }

    [UnixFact]
    public void Undo_ExistedFalseThroughRelinkedWtf_DoesNotDeleteOutsideConfig()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data", "enUS"));
        File.WriteAllText(env.InClient("Data", "enUS", "realmlist.wtf"), OldRealmlist);
        // Switch creates Config.wtf (did not exist).
        Assert.NotNull(env.Applier.Apply(env.Applier.Plan(env.Request("Realm"))).BackupId);
        Assert.True(File.Exists(env.InClient("WTF", "Config.wtf")));

        // Relink WTF outside with a Config.wtf that undo would try to delete (existed:false).
        Directory.Move(env.InClient("WTF"), env.InOutside("WTF-real"));
        Directory.CreateDirectory(env.InOutside("WTF"));
        File.WriteAllText(env.InOutside("WTF", "Config.wtf"), "SET realmName \"Planted\"\r\n");
        Directory.CreateSymbolicLink(env.InClient("WTF"), env.InOutside("WTF"));
        var outsideBefore = Snapshot(env.Outside);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.True(File.Exists(env.InOutside("WTF", "Config.wtf")));
        Assert.Equal(outsideBefore, Snapshot(env.Outside));
    }

    [UnixFact]
    public void Apply_FileSymlinkRealmlistInsideClient_StillWorks()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data", "enUS"));
        Directory.CreateDirectory(env.InClient("shared"));
        var shared = env.InClient("shared", "realmlist.wtf");
        File.WriteAllText(shared, OldRealmlist);
        File.CreateSymbolicLink(env.InClient("Data", "enUS", "realmlist.wtf"), Path.Combine("..", "..", "shared", "realmlist.wtf"));

        var result = env.Applier.Apply(env.Applier.Plan(env.Request()));

        Assert.NotNull(result.BackupId);
        Assert.Contains("logon-a.example.org", File.ReadAllText(env.InClient("Data", "enUS", "realmlist.wtf")), StringComparison.Ordinal);
        // Shared target was not written through; Apply replaces the link with a regular file.
        Assert.Null(new FileInfo(env.InClient("Data", "enUS", "realmlist.wtf")).LinkTarget);
        Assert.Equal(OldRealmlist, File.ReadAllText(shared));
    }
}
