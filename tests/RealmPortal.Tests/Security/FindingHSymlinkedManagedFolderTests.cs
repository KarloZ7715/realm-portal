using System.Text;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

/// <summary>
/// QA (PR #58 @ 802f884): a managed folder (Data/enUS, WTF) that is a symbolic link to a folder outside the client must not let
/// switch or undo write or delete files outside the client root. Unix only (creating links on Windows needs privileges).
/// </summary>
public class FindingHSymlinkedManagedFolderTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private const string OldRealmlist = "set realmlist old.example.org\r\n";
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
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunningProbe(), Backups);
        }

        public ClientFilesPlan Plan(string realm = "") =>
            Applier.Plan(new ClientFilesRequest("install-1", "profile-1", Client, V3, BlobA, realm));

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
    public void Apply_WithDataLocaleFolderLinkedOutside_RefusesAndWritesNothingOutside()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data"));
        Directory.CreateDirectory(env.InOutside("enUS"));
        File.WriteAllText(env.InOutside("enUS", "realmlist.wtf"), OldRealmlist);
        Directory.CreateSymbolicLink(env.InClient("Data", "enUS"), env.InOutside("enUS"));
        var outsideBefore = Snapshot(env.Outside);

        var plan = env.Plan();
        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(outsideBefore, Snapshot(env.Outside));
    }

    [UnixFact]
    public void Apply_WithWtfFolderLinkedOutside_RefusesAndWritesNothingOutside()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data", "enUS"));
        File.WriteAllText(env.InClient("Data", "enUS", "realmlist.wtf"), OldRealmlist);
        Directory.CreateDirectory(env.InOutside("WTF"));
        File.WriteAllText(env.InOutside("WTF", "Config.wtf"), "SET realmName \"Old\"\r\n");
        Directory.CreateSymbolicLink(env.InClient("WTF"), env.InOutside("WTF"));
        var outsideBefore = Snapshot(env.Outside);
        var realmlistBefore = File.ReadAllText(env.InClient("Data", "enUS", "realmlist.wtf"));

        var plan = env.Plan("Realm");
        Assert.Contains(plan.Edits, e => e.RelativePath.EndsWith("Config.wtf", StringComparison.Ordinal));
        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(outsideBefore, Snapshot(env.Outside));
        Assert.Equal(realmlistBefore, File.ReadAllText(env.InClient("Data", "enUS", "realmlist.wtf")));
    }

    [UnixFact]
    public void Apply_WithLinkedDataFolderLinkedThroughTwoHops_RefusesWhenTheLastHopLeavesTheClient()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data"));
        Directory.CreateDirectory(env.InOutside("enUS"));
        File.WriteAllText(env.InOutside("enUS", "realmlist.wtf"), OldRealmlist);
        // Data/enUS -> ./hop (inside, relative) -> outside/enUS (absolute)
        Directory.CreateSymbolicLink(env.InClient("hop"), env.InOutside("enUS"));
        Directory.CreateSymbolicLink(env.InClient("Data", "enUS"), Path.Combine("..", "hop"));
        var outsideBefore = Snapshot(env.Outside);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(env.Plan()));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(outsideBefore, Snapshot(env.Outside));
    }

    [UnixFact]
    public void Apply_WithManagedFolderLinkedInsideTheClient_StillWorks()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data"));
        Directory.CreateDirectory(env.InClient("locales", "enUS"));
        File.WriteAllText(env.InClient("locales", "enUS", "realmlist.wtf"), OldRealmlist);
        Directory.CreateSymbolicLink(env.InClient("Data", "enUS"), Path.Combine("..", "locales", "enUS"));

        var result = env.Applier.Apply(env.Plan());

        Assert.NotNull(result.BackupId);
        Assert.Contains("logon-a.example.org", File.ReadAllText(env.InClient("locales", "enUS", "realmlist.wtf")));
    }

    [UnixFact]
    public void Undo_WithDataLocaleFolderRelinkedOutside_RefusesAndWritesNothing()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data", "enUS"));
        File.WriteAllText(env.InClient("Data", "enUS", "realmlist.wtf"), OldRealmlist);
        env.Applier.Apply(env.Plan());
        var applied = File.ReadAllBytes(env.InClient("Data", "enUS", "realmlist.wtf"));

        // Move the real folder away and point Data/enUS at an outside folder holding the switched bytes.
        Directory.Move(env.InClient("Data", "enUS"), env.InClient("Data", "enUS.real"));
        Directory.CreateDirectory(env.InOutside("enUS"));
        File.WriteAllBytes(env.InOutside("enUS", "realmlist.wtf"), applied);
        Directory.CreateSymbolicLink(env.InClient("Data", "enUS"), env.InOutside("enUS"));
        var outsideBefore = Snapshot(env.Outside);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(outsideBefore, Snapshot(env.Outside));
        Assert.Equal(applied, File.ReadAllBytes(env.InClient("Data", "enUS.real", "realmlist.wtf")));
    }

    [UnixFact]
    public void Undo_WithWtfFolderRelinkedOutside_DoesNotDeleteOutsideConfig()
    {
        using var env = new Env();
        Directory.CreateDirectory(env.InClient("Data", "enUS"));
        File.WriteAllText(env.InClient("Data", "enUS", "realmlist.wtf"), OldRealmlist);
        var plan = env.Plan("Realm");
        Assert.Contains(plan.Edits, e => e.RelativePath == "WTF/Config.wtf" && e.Kind == ClientEditKind.Create);
        env.Applier.Apply(plan);
        var appliedConfig = File.ReadAllBytes(env.InClient("WTF", "Config.wtf"));
        var appliedRealmlist = File.ReadAllBytes(env.InClient("Data", "enUS", "realmlist.wtf"));

        // Config.wtf did not exist before the switch, so undo would delete it: point WTF at an outside folder instead.
        Directory.Move(env.InClient("WTF"), env.InClient("WTF.real"));
        Directory.CreateDirectory(env.InOutside("WTF"));
        File.WriteAllBytes(env.InOutside("WTF", "Config.wtf"), appliedConfig);
        Directory.CreateSymbolicLink(env.InClient("WTF"), env.InOutside("WTF"));
        var outsideBefore = Snapshot(env.Outside);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.True(File.Exists(env.InOutside("WTF", "Config.wtf")));
        Assert.Equal(outsideBefore, Snapshot(env.Outside));
        // Nothing inside the client was restored either (all paths are checked before the first write).
        Assert.Equal(appliedRealmlist, File.ReadAllBytes(env.InClient("Data", "enUS", "realmlist.wtf")));
    }

    [UnixFact]
    public void PortalSwitch_WithDataLocaleFolderLinkedOutside_ReturnsPathEscapesClient()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        var outside = dir.Combine("outside");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(Path.Combine(client, "Data"));
        Directory.CreateDirectory(Path.Combine(outside, "enUS"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllText(Path.Combine(outside, "enUS", "realmlist.wtf"), OldRealmlist);
        Directory.CreateSymbolicLink(Path.Combine(client, "Data", "enUS"), Path.Combine(outside, "enUS"));
        var outsideBefore = Snapshot(outside);

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunningProbe(), _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;

        var result = service.SwitchServer(new SwitchRequest(clientId, a.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.PathEscapesClient, result.Error!.Code);
        Assert.Equal(outsideBefore, Snapshot(outside));
        Assert.False(Directory.Exists(Path.Combine(client, "WTF")));
    }

    private sealed class NeverRunningProbe : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
