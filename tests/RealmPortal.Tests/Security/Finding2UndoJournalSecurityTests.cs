using System.Text;
using System.Text.Json;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding2UndoJournalSecurityTests
{
    [Fact]
    public void Undo_MarksBackupUndoneOnlyAfterStateIsCommitted()
    {
        using var env = new UndoEnv();
        env.SwitchToB();

        var journalPath = Path.Combine(env.Config, "last-switch.json");
        var journal = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(journalPath));
        var backupId = journal.GetProperty("backup_id").GetString()!;

        Assert.True(env.Service.Undo().Succeeded);
        var manifest = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(env.Backups, env.ClientId, backupId, "manifest.json")));
        Assert.True(manifest.TryGetProperty("undone_at", out var undone) && undone.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void Startup_RecoversPendingStateUndo_WithoutRerunningFileUndo()
    {
        using var env = new UndoEnv();
        env.SwitchToB();
        var journalPath = Path.Combine(env.Config, "last-switch.json");
        var journal = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(journalPath));
        var backupId = journal.GetProperty("backup_id").GetString()!;

        var client = env.Service.AddClient(env.Client).Value!;
        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), env.Backups);
        applier.Undo(client.Id, env.Client, backupId, markUndone: false);
        File.WriteAllText(journalPath, JsonSerializer.Serialize(new
        {
            version = 1,
            client_id = env.ClientId,
            backup_id = backupId,
            files_undone = true,
        }) + "\n");

        var tampered = "set realmlist tampered.example.org\r\n";
        File.WriteAllBytes(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1.GetBytes(tampered));

        var recovered = new PortalService(env.Config, env.Projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => env.Backups);
        Assert.Equal(env.ServerB.Id, recovered.GetStatus().Value!.Active!.Id);
        Assert.True(recovered.GetStatus().Value!.CanUndo);
        Assert.Contains("tampered.example.org", File.ReadAllText(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf")));
        Assert.True(File.Exists(journalPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_WhenBackupAlreadySettled_SucceedsAndClearsJournal(bool deleteManifest)
    {
        using var env = new UndoEnv();
        env.SwitchToB();

        var journalPath = Path.Combine(env.Config, "last-switch.json");
        var journal = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(journalPath));
        var backupId = journal.GetProperty("backup_id").GetString()!;
        var manifestPath = Path.Combine(env.Backups, env.ClientId, backupId, "manifest.json");

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), env.Backups);
        applier.Undo(env.ClientId, env.Client, backupId, markUndone: false);
        if (deleteManifest)
            File.Delete(manifestPath);
        else
            applier.MarkBackupUndone(env.ClientId, backupId);

        File.WriteAllText(journalPath, JsonSerializer.Serialize(new
        {
            version = 1,
            client_id = env.ClientId,
            backup_id = backupId,
            files_undone = true,
        }) + "\n");

        var undo = env.Service.Undo();
        Assert.True(undo.Succeeded, undo.Error?.Diagnostic);
        Assert.Equal(env.ServerA.Id, undo.Value!.Active?.Id);
        Assert.False(File.Exists(journalPath));
        if (!deleteManifest)
        {
            var manifest = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(manifestPath));
            Assert.True(manifest.TryGetProperty("undone_at", out var undone) && undone.ValueKind != JsonValueKind.Null);
        }
    }

    private sealed class UndoEnv : IDisposable
    {
        private readonly TempDirectory _dir = new();

        public UndoEnv()
        {
            Config = _dir.Combine("config");
            Projection = _dir.Combine("projection");
            Client = _dir.Combine("client");
            Backups = _dir.Combine("backups");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Projection);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Service = new PortalService(Config, Projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => Backups);
            ClientId = Service.AddClient(Client).Value!.Id;
            ServerA = Service.AddServer(new NewServer("A", "a.example.org")).Value!;
            ServerB = Service.AddServer(new NewServer("B", "b.example.org")).Value!;
            File.WriteAllBytes(Path.Combine(Client, "Data", "enUS", "realmlist.wtf"), "set realmlist old.example.org\r\n"u8.ToArray());
        }

        public string Config { get; }
        public string Projection { get; }
        public string Client { get; }
        public string Backups { get; }
        public PortalService Service { get; }
        public string ClientId { get; }
        public ServerEntry ServerA { get; }
        public ServerEntry ServerB { get; }

        public void SwitchToB()
        {
            Assert.True(Service.SwitchServer(new SwitchRequest(ClientId, ServerA.Id)).Succeeded);
            Assert.True(Service.SwitchServer(new SwitchRequest(ClientId, ServerB.Id)).Succeeded);
        }

        public void Dispose() => _dir.Dispose();
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class NoLaunch : RealmPortal.Core.Launch.IGameLaunchService
    {
        public void Launch(RealmPortal.Core.Launch.GameLaunchSpec spec) { }
    }
}
