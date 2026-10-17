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

        public ApplierEnv()
        {
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunningProbe(), Backups);
        }

        public ClientFilesPlan Plan() =>
            Applier.Plan(new ClientFilesRequest("install-1", "profile-1", Client, V3, BlobA, ""));

        public void Put(string relative, string text)
        {
            var path = Path.Combine([Client, .. relative.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Dispose() => _temp.Dispose();
    }

    private sealed class NeverRunningProbe : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

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

}
