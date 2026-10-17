using System.Text.Json;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class FindingM1UndoRollbackJournalTests
{
    [Fact]
    public void Undo_WhenActivoWriteFails_ClearsFilesUndoneAndRollbackBackup_ThenSecondUndoRestoresClient()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        var realmlist = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.WriteAllBytes(realmlist, "set realmlist orig.example.org\r\n"u8.ToArray());

        var writer = new FailActivoWriteOnUndo(projection);
        var service = new PortalService(
            config,
            projection,
            new PeClientVersionReader(),
            new NoLaunch(),
            new NeverRunning(),
            _ => backups,
            writer);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);
        var clientBackups = Path.Combine(backups, clientId);
        var pendingBefore = PendingBackupIds(clientBackups);

        writer.FailNextActivoWrite();
        var failed = service.Undo();
        Assert.False(failed.Succeeded);
        Assert.Equal(Consistency.RolledBack, failed.Error!.Consistency);
        Assert.Equal(b.Id, service.GetStatus().Value!.Active!.Id);
        Assert.Contains("b.example.org", File.ReadAllText(realmlist));

        using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "last-switch.json")));
        Assert.False(journal.RootElement.GetProperty("files_undone").GetBoolean());
        Assert.Equal(clientId, journal.RootElement.GetProperty("client_id").GetString());
        var backupId = journal.RootElement.GetProperty("backup_id").GetString();
        Assert.False(string.IsNullOrEmpty(backupId));

        // Each switch leaves its own pending backup. The re-apply backup from this failed undo must not
        // add another one (it is marked undone); the journal backup stays pending for the retry.
        var pending = PendingBackupIds(clientBackups);
        Assert.Equal(
            pendingBefore.OrderBy(id => id, StringComparer.Ordinal),
            pending.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(backupId, Assert.Single(pending, id => id == backupId));

        var undo = service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(a.Id, service.GetStatus().Value!.Active!.Id);
        Assert.Contains("a.example.org", File.ReadAllText(realmlist));
    }

    private static List<string> PendingBackupIds(string clientBackupDir)
    {
        var pending = new List<string>();
        foreach (var manifestPath in Directory.GetFiles(clientBackupDir, "manifest.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.GetProperty("undone_at").ValueKind == JsonValueKind.Null)
                pending.Add(doc.RootElement.GetProperty("backup_id").GetString()!);
        }

        return pending;
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    /// <summary>Counts writes of <c>activo.json</c> and fails the next one after <see cref="FailNextActivoWrite"/>.</summary>
    private sealed class FailActivoWriteOnUndo(string projectionDir) : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();
        private readonly string _activoPath = ActiveProjection.FilePath(projectionDir);
        private int _activoWrites;
        private int _failAt = -1;

        public void FailNextActivoWrite() => _failAt = _activoWrites + 1;

        public void WriteAllBytes(string path, byte[] content)
        {
            if (path == _activoPath)
            {
                _activoWrites++;
                if (_activoWrites == _failAt)
                    throw new IOException("Injected activo.json write failure.");
            }

            _inner.WriteAllBytes(path, content);
        }
    }
}
