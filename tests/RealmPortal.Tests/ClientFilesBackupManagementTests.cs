using System.Text;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public sealed class ClientFilesBackupManagementTests
{
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));
    private sealed class Probe : IGameProcessProbe
    {
        public bool Running { get; set; }
        public bool IsGameRunning(string clientRoot) => Running;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDirectory _temp = new();
        public Fixture(int keepBackups = ClientFilesApplier.DefaultKeepBackups)
        {
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Directory.CreateDirectory(Path.Combine(Client, "WTF"));
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), Probe, Backups, keepBackups: keepBackups);
        }
        public string Client { get; }
        public string Backups { get; }
        public Probe Probe { get; } = new();
        public ClientFilesApplier Applier { get; }
        public string Realmlist => Path.Combine(Client, "Data", "enUS", "realmlist.wtf");
        public string Config => Path.Combine(Client, "WTF", "Config.wtf");
        public void Dispose() => _temp.Dispose();
    }

    [Fact]
    public void CreateBackup_DeduplicatesIdenticalSnapshot_AndListIsNewestFirst()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Realmlist, "set realmlist one.example\r\n", Encoding.ASCII);
        File.WriteAllText(f.Config, "SET realmName \"One\"\r\n", Encoding.ASCII);

        var first = f.Applier.CreateBackup("client-1", f.Client, "before edit");
        var second = f.Applier.CreateBackup("client-1", f.Client, "ignored duplicate label");
        var list = f.Applier.ListBackups("client-1");

        Assert.False(first.WasDeduplicated);
        Assert.True(second.WasDeduplicated);
        Assert.Equal(first.BackupId, second.BackupId);
        Assert.Single(list);
        Assert.Equal(2, list[0].Files.Count);
        Assert.Equal("before edit", list[0].Label);
    }

    [Fact]
    public void CreateBackup_IgnoresDamagedLatestSnapshotDuringDeduplication()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Realmlist, "set realmlist first.example\r\n", Encoding.ASCII);
        var first = f.Applier.CreateBackup("client-1", f.Client);
        File.WriteAllText(f.Realmlist, "set realmlist second.example\r\n", Encoding.ASCII);
        var damaged = f.Applier.CreateBackup("client-1", f.Client);
        File.WriteAllText(Path.Combine(f.Backups, "client-1", damaged.BackupId, "manifest.json"), "{");

        var next = f.Applier.CreateBackup("client-1", f.Client);

        Assert.NotEqual(first.BackupId, next.BackupId);
        Assert.NotEqual(damaged.BackupId, next.BackupId);
        Assert.Contains(f.Applier.ListBackups("client-1"), x => x.BackupId == damaged.BackupId && x.Error is not null);
    }

    [Fact]
    public void RestoreBackup_WithForce_IgnoresDamagedLatestSnapshotWhenSavingSafetyBackup()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Realmlist, "set realmlist original.example\r\n", Encoding.ASCII);
        var target = f.Applier.CreateBackup("client-1", f.Client);
        File.WriteAllText(f.Realmlist, "set realmlist current.example\r\n", Encoding.ASCII);
        var damaged = f.Applier.CreateBackup("client-1", f.Client);
        File.WriteAllText(Path.Combine(f.Backups, "client-1", damaged.BackupId, "manifest.json"), "{");

        var restored = f.Applier.RestoreBackup("client-1", f.Client, target.BackupId, force: true);

        Assert.NotNull(restored.SafetyBackupId);
        Assert.Equal("set realmlist original.example\r\n", File.ReadAllText(f.Realmlist));
        Assert.Contains(f.Applier.ListBackups("client-1"), x => x.BackupId == damaged.BackupId && x.Error is not null);
    }

    [Fact]
    public void PruneBackups_IgnoresDamagedEntriesAndKeepsPendingApplySnapshot()
    {
        using var f = new Fixture(keepBackups: 1);
        File.WriteAllText(f.Realmlist, "set realmlist original.example\r\n", Encoding.ASCII);
        var plan = f.Applier.Plan(new ClientFilesRequest("client-1", "server-1", f.Client, V3,
            "set realmlist switched.example\r\n", "Switched"));
        var pending = f.Applier.Apply(plan).BackupId!;
        File.WriteAllText(f.Realmlist, "set realmlist manual.example\r\n", Encoding.ASCII);
        var manual = f.Applier.CreateBackup("client-1", f.Client);
        File.WriteAllText(Path.Combine(f.Backups, "client-1", manual.BackupId, "manifest.json"), "{");

        f.Applier.PruneBackups("client-1", keepBackups: 1);

        Assert.Contains(f.Applier.ListBackups("client-1"), x => x.BackupId == pending && x.Error is null);
        Assert.Contains(f.Applier.ListBackups("client-1"), x => x.BackupId == manual.BackupId && x.Error is not null);
    }

    [Fact]
    public void RestoreBackup_PreflightsEveryTargetBeforeWritingAny()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Realmlist, "set realmlist original.example\r\n", Encoding.ASCII);
        File.WriteAllText(f.Config, "SET realmName \"Original\"\r\n", Encoding.ASCII);
        var plan = f.Applier.Plan(new ClientFilesRequest("client-1", "server-1", f.Client, V3,
            "set realmlist switched.example\r\n", "Switched"));
        var applied = f.Applier.Apply(plan);
        File.WriteAllText(f.Config, "SET realmName \"Changed\"\r\n", Encoding.ASCII);

        var currentRealmlist = File.ReadAllText(f.Realmlist);
        var error = Assert.Throws<ClientFilesException>(() => f.Applier.RestoreBackup("client-1", f.Client, applied.BackupId!));

        Assert.Equal(ClientFilesErrorCode.UndoConflict, error.Code);
        Assert.Equal(currentRealmlist, File.ReadAllText(f.Realmlist));
        Assert.Equal("SET realmName \"Changed\"\r\n", File.ReadAllText(f.Config));
    }

    [Fact]
    public void RestoreBackup_WithForce_CreatesSafetyBackupBeforeRestoringSnapshot()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Realmlist, "set realmlist original.example\r\n", Encoding.ASCII);
        var backup = f.Applier.CreateBackup("client-1", f.Client);
        File.WriteAllText(f.Realmlist, "set realmlist current.example\r\n", Encoding.ASCII);

        var result = f.Applier.RestoreBackup("client-1", f.Client, backup.BackupId, force: true);

        Assert.NotNull(result.SafetyBackupId);
        Assert.Equal("set realmlist original.example\r\n", File.ReadAllText(f.Realmlist));
        Assert.Contains(f.Applier.ListBackups("client-1"), x => x.BackupId == result.SafetyBackupId && x.Label == "pre-restore");
    }

    [Fact]
    public void Undo_KeepsPendingApplyBackupWhenPruningManualSnapshots()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Realmlist, "set realmlist original.example\r\n", Encoding.ASCII);
        var plan = f.Applier.Plan(new ClientFilesRequest("client-1", "server-1", f.Client, V3,
            "set realmlist switched.example\r\n", "Switched"));
        f.Applier.Apply(plan);

        for (var i = 1; i <= 10; i++)
        {
            File.WriteAllText(f.Realmlist, $"set realmlist manual-{i}.example\r\n", Encoding.ASCII);
            f.Applier.CreateBackup("client-1", f.Client);
        }

        File.WriteAllText(f.Realmlist, "set realmlist switched.example\r\n", Encoding.ASCII);
        var result = f.Applier.Undo("client-1", f.Client);

        Assert.Equal("set realmlist original.example\r\n", File.ReadAllText(f.Realmlist));
        Assert.Contains(result.Files, x => x.Status == UndoFileStatus.Restored);
    }

    [Fact]
    public void DeleteBackup_RemovesOnlyRequestedSnapshot()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Realmlist, "set realmlist first.example\r\n", Encoding.ASCII);
        var first = f.Applier.CreateBackup("client-1", f.Client);
        File.WriteAllText(f.Realmlist, "set realmlist second.example\r\n", Encoding.ASCII);
        var second = f.Applier.CreateBackup("client-1", f.Client);

        f.Applier.DeleteBackup("client-1", first.BackupId);

        Assert.Single(f.Applier.ListBackups("client-1"));
        Assert.Equal(second.BackupId, f.Applier.ListBackups("client-1")[0].BackupId);
    }

    [Fact]
    public void CreateBackup_RejectsManagedFilesOverOneMiBWithoutCreatingSnapshot()
    {
        using var f = new Fixture();
        File.WriteAllBytes(f.Realmlist, new byte[ClientFilesApplier.MaxManagedConfigBytes + 1]);

        var error = Assert.Throws<ClientFilesException>(() => f.Applier.CreateBackup("client-1", f.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, error.Code);
        Assert.Empty(f.Applier.ListBackups("client-1"));
    }
}
