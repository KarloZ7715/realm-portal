using System.Text;
using System.Text.Json;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientPathsTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    private sealed class ApplierEnv : IDisposable
    {
        private readonly TempDirectory _temp = new();
        public string Client { get; }
        public string Backups { get; }
        public ClientFilesApplier Applier { get; }

        /// <summary>Creates isolated client and backup paths with an applier whose game probe is always idle.</summary>
        public ApplierEnv()
        {
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunningProbe(), Backups);
        }

        /// <summary>Plans the fixture's realm-address change for its version 3 client.</summary>
        public ClientFilesPlan Plan() =>
            Applier.Plan(new ClientFilesRequest("install-1", "profile-1", Client, V3, BlobA, ""));

        /// <summary>Seeds a file below the client directory, creating its parent folders.</summary>
        public void Put(string relative, string text)
        {
            var path = Path.Combine([Client, .. relative.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        /// <summary>Removes the fixture's temporary client and backup files.</summary>
        public void Dispose() => _temp.Dispose();
    }

    private sealed class NeverRunningProbe : IGameProcessProbe
    {
        /// <summary>Always reports an idle game so tests can exercise file validation.</summary>
        public bool IsGameRunning(string clientRoot) => false;
    }

    /// <summary>Verifies that a substituted manifest root fails verification without changing files in the substituted directory.</summary>
    [Fact]
    public void Undo_RejectsTamperedClientRoot_InManifest()
    {
        using var env = new ApplierEnv();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Applier.Apply(env.Plan());

        using var victim = new TempDirectory();
        var victimFile = victim.Combine("important.txt");
        File.WriteAllText(victimFile, "victim");
        var manifestPath = Directory.GetFiles(Path.Combine(env.Backups, "install-1"), "manifest.json", SearchOption.AllDirectories).Single();
        var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var prop in json.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("client_root"))
                    writer.WriteString("client_root", victim.Path);
                else
                    prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        File.WriteAllBytes(manifestPath, stream.ToArray());

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));
        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal("victim", File.ReadAllText(victimFile));
    }

    /// <summary>Verifies that undo rejects a manifest entry outside the managed client paths.</summary>
    [Fact]
    public void Undo_RejectsManifestPathOutsideAllowList()
    {
        using var env = new ApplierEnv();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        var result = env.Applier.Apply(env.Plan());
        var manifestPath = Path.Combine(env.Backups, "install-1", result.BackupId!, "manifest.json");
        var text = File.ReadAllText(manifestPath).Replace("Data/enUS/realmlist.wtf", "secrets/passwords.txt");
        File.WriteAllText(manifestPath, text);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));
        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
    }

    /// <summary>Verifies that an invalid later manifest entry blocks undo before an earlier valid file is restored.</summary>
    [Fact]
    public void Undo_WithInvalidLaterManifestPath_DoesNotPartiallyRestore()
    {
        using var env = new ApplierEnv();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Put("WTF/Config.wtf", "SET x \"y\"\r\n");
        var plan = env.Applier.Plan(new ClientFilesRequest(
            "install-1", "profile-1", env.Client, V3, BlobA, "Realm"));
        var result = env.Applier.Apply(plan);
        var appliedRealmlist = File.ReadAllText(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf"));

        var manifestPath = Path.Combine(env.Backups, "install-1", result.BackupId!, "manifest.json");
        var text = File.ReadAllText(manifestPath).Replace("WTF/Config.wtf", "secrets/passwords.txt");
        File.WriteAllText(manifestPath, text);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));
        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(appliedRealmlist, File.ReadAllText(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf")));
    }

    /// <summary>Verifies that canonicalization preserves the root separator on Unix platforms.</summary>
    [Fact]
    public void CanonicalRoot_PreservesUnixFilesystemRoot()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        Assert.Equal("/", ClientPaths.CanonicalRoot("/"));
    }

    /// <summary>Verifies that canonicalization preserves the trailing separator of a Windows drive root.</summary>
    [Fact]
    public void CanonicalRoot_PreservesWindowsDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var drive = Path.GetPathRoot(Environment.SystemDirectory)!;
        Assert.Equal(drive.TrimEnd('\\') + '\\', ClientPaths.CanonicalRoot(drive));
    }

}
