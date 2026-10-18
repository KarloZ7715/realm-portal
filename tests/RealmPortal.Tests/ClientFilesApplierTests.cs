using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientFilesApplierTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private const string BlobB = "set realmlist logon-b.example.org\r\n";

    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    private sealed class FakeProbe : IGameProcessProbe
    {
        public bool Running { get; set; }

        public bool IsGameRunning(string clientRoot) => Running;
    }

    /// <summary>Delegates to the real writer but fails (or corrupts) the writes that match a predicate; only client files are counted.</summary>
    private sealed class FaultyWriter(string clientRoot) : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();
        public int ClientWrites { get; private set; }
        public Func<int, bool> Fail { get; set; } = _ => false;
        public Func<int, bool> Corrupt { get; set; } = _ => false;

        public void WriteAllBytes(string path, byte[] content)
        {
            if (Path.GetFullPath(path).StartsWith(Path.GetFullPath(clientRoot), StringComparison.Ordinal))
            {
                ClientWrites++;
                if (Fail(ClientWrites))
                    throw new IOException("Injected write failure.");
                if (Corrupt(ClientWrites))
                    content = [.. content, (byte)'!'];
            }

            _inner.WriteAllBytes(path, content);
        }
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();
        private DateTimeOffset _now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

        public Env()
        {
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Writer = new FaultyWriter(Client);
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), Writer, Probe, Backups, () => _now = _now.AddSeconds(1), TimeSpan.FromMilliseconds(300));
        }

        public string Client { get; }
        public string Backups { get; }
        public FakeProbe Probe { get; } = new();
        public FaultyWriter Writer { get; }
        public ClientFilesApplier Applier { get; }

        public string Path_(string relative) => Path.Combine([Client, .. relative.Split('/')]);

        public void Put(string relative, byte[] bytes)
        {
            var path = Path_(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Put(string relative, string text) => Put(relative, Encoding.Latin1.GetBytes(text));

        public ClientFilesPlan Plan(string blob = BlobA, string realm = "") =>
            Applier.Plan(new ClientFilesRequest("install-1", "profile-1", Client, V3, blob, realm));

        public Dictionary<string, string> Files() =>
            Directory.EnumerateFiles(Client, "*", SearchOption.AllDirectories)
                .ToDictionary(p => System.IO.Path.GetRelativePath(Client, p), p => Convert.ToHexString(File.ReadAllBytes(p)));

        public void Dispose() => _temp.Dispose();
    }

    private static string[] BackupFolders(Env env) =>
        Directory.Exists(env.Backups) ? Directory.GetDirectories(env.Backups).SelectMany(d => Directory.GetDirectories(d)).ToArray() : [];

    [Fact]
    public void Undo_AfterApply_RestoresEveryFileByteForByte()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Put("WTF/Config.wtf", [0x53, 0x45, 0x54, 0x20, 0x78, 0x20, 0x22, 0xE9, 0x22, 0x0D, 0x0A, 0x53, 0x45, 0x54, 0x20, 0x72, 0x65, 0x61, 0x6C, 0x6D, 0x4C, 0x69, 0x73, 0x74, 0x20, 0x22, 0x6F, 0x6C, 0x64, 0x22]);
        var before = env.Files();

        var result = env.Applier.Apply(env.Plan(BlobA, "Realm"));

        Assert.NotNull(result.BackupId);
        Assert.Equal(2, result.Edits.Count);
        Assert.NotEqual(before, env.Files());
        Assert.Contains("logon-a.example.org", File.ReadAllText(env.Path_("Data/enUS/realmlist.wtf")));

        var undo = env.Applier.Undo("install-1", env.Client);

        Assert.Equal(result.BackupId, undo.BackupId);
        Assert.All(undo.Files, f => Assert.Equal(UndoFileStatus.Restored, f.Status));
        Assert.Equal(before, env.Files());
    }

    [Fact]
    public void Undo_DeletesFilesThatDidNotExist_AndKeepsFolders()
    {
        using var env = new Env();
        var before = env.Files(); // empty client with only Data/enUS

        var result = env.Applier.Apply(env.Plan());
        Assert.True(File.Exists(env.Path_("WTF/Config.wtf")));

        var undo = env.Applier.Undo("install-1", env.Client);

        Assert.All(undo.Files, f => Assert.Equal(UndoFileStatus.Deleted, f.Status));
        Assert.Equal(before, env.Files());
        Assert.True(Directory.Exists(env.Path_("WTF")));
        Assert.NotNull(result.BackupId);
    }

    [Fact]
    public void Apply_WithAnEmptyPlan_DoesNothingAndMakesNoBackup()
    {
        using var env = new Env();
        env.Applier.Apply(env.Plan());
        var plan = env.Plan();
        Assert.True(plan.IsEmpty);
        var backupsBefore = Directory.GetDirectories(Path.Combine(env.Backups, "install-1")).Length;

        var result = env.Applier.Apply(plan);

        Assert.Null(result.BackupId);
        Assert.Empty(result.Edits);
        Assert.Equal(backupsBefore, Directory.GetDirectories(Path.Combine(env.Backups, "install-1")).Length);
    }

    [Fact]
    public void Plan_WritesNothing_NotEvenTheBackupRoot()
    {
        using var env = new Env();
        var before = env.Files();

        env.Plan();

        Assert.Equal(before, env.Files());
        Assert.False(Directory.Exists(env.Backups));
    }

    [Fact]
    public void Apply_AndUndo_WhenGameRunning_BlockWithoutTouchingAnything()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        var plan = env.Plan();
        var before = env.Files();

        env.Probe.Running = true;
        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));

        Assert.Equal(ClientFilesErrorCode.GameRunning, ex.Code);
        Assert.Equal(before, env.Files());
        Assert.Empty(BackupFolders(env));

        env.Probe.Running = false;
        env.Applier.Apply(plan);
        env.Probe.Running = true;
        var undo = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));
        Assert.Equal(ClientFilesErrorCode.GameRunning, undo.Code);
    }

    [Fact]
    public void Apply_FailsWithHashMismatch_WhenAFileChangedSincePlanning()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        var plan = env.Plan();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist someone-else.example.org\r\n");
        var before = env.Files();

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.Equal(before, env.Files());
        Assert.Empty(BackupFolders(env));
    }

    [Fact]
    public void Apply_FailsWithHashMismatch_WhenAFileAppearedSincePlanning()
    {
        using var env = new Env();
        var plan = env.Plan(); // plans to create both files
        env.Put("WTF/Config.wtf", "SET x \"1\"\r\n");

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_OnMidWriteFailure_RevertsEverythingAndLeavesNoBackup(int failingWrite)
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Put("WTF/Config.wtf", "SET realmList \"old\"\r\n");
        var plan = env.Plan();
        var before = env.Files();
        env.Writer.Fail = n => n == failingWrite;

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));

        Assert.Equal(ClientFilesErrorCode.PermissionDenied, ex.Code);
        Assert.Equal(before, env.Files()); // old content everywhere, no temp files left
        Assert.Empty(Directory.GetDirectories(Path.Combine(env.Backups, "install-1")));
        Assert.Equal(ClientFilesErrorCode.NothingToUndo, Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client)).Code);
    }

    [Fact]
    public void Apply_OnMidWriteFailureOnNewFiles_RemovesTheOnesAlreadyCreated()
    {
        using var env = new Env();
        var before = env.Files();
        env.Writer.Fail = n => n == 2;

        Assert.Throws<ClientFilesException>(() => env.Applier.Apply(env.Plan()));

        Assert.Equal(before, env.Files());
    }

    [Fact]
    public void Apply_OnCorruptedWrite_IsDetectedAndReverted()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        var plan = env.Plan();
        var before = env.Files();
        env.Writer.Corrupt = n => n == 1;

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal(before, env.Files());
    }

    [Fact]
    public void Apply_WhenRevertFails_BackupStaysForUndo()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Put("WTF/Config.wtf", "SET realmList \"old\"\r\n");
        var plan = env.Plan();
        var before = env.Files();
        env.Writer.Fail = n => n >= 2; // second file fails and the revert of the first one fails as well

        Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));
        Assert.NotEqual(before, env.Files());

        env.Writer.Fail = _ => false;
        var undo = env.Applier.Undo("install-1", env.Client);

        Assert.Equal(before, env.Files());
        Assert.Contains(undo.Files, f => f.Status == UndoFileStatus.Restored);
        Assert.Contains(undo.Files, f => f.Status == UndoFileStatus.AlreadyRestored);
    }

    [Fact]
    public void Undo_ReportsConflicts_AndNeverOverwritesAChangedFile()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Put("WTF/Config.wtf", "SET realmList \"old\"\r\n");
        env.Applier.Apply(env.Plan());
        env.Put("WTF/Config.wtf", "SET realmList \"edited by the game\"\r\n");

        var undo = env.Applier.Undo("install-1", env.Client);

        Assert.True(undo.HasConflicts);
        Assert.Equal(UndoFileStatus.Conflict, undo.Files.Single(f => f.RelativePath == "WTF/Config.wtf").Status);
        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Equal("SET realmList \"edited by the game\"\r\n", File.ReadAllText(env.Path_("WTF/Config.wtf")));
        Assert.Equal("set realmlist old.example.org\r\n", File.ReadAllText(env.Path_("Data/enUS/realmlist.wtf")));

        // Still pending: asking again reports the same conflict instead of undoing something older.
        var again = env.Applier.Undo("install-1", env.Client);
        Assert.True(again.HasConflicts);
        Assert.Equal(undo.BackupId, again.BackupId);
    }

    [Fact]
    public void Undo_WithNothingApplied_ThrowsNothingToUndo_AndAlsoAfterTheLastUndo()
    {
        using var env = new Env();
        Assert.Equal(ClientFilesErrorCode.NothingToUndo, Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client)).Code);

        env.Applier.Apply(env.Plan());
        env.Applier.Undo("install-1", env.Client);

        Assert.Equal(ClientFilesErrorCode.NothingToUndo, Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client)).Code);
    }

    [Fact]
    public void Undo_UnwindsSuccessiveAppliesOneByOne()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        var original = env.Files();
        env.Applier.Apply(env.Plan(BlobA));
        var afterA = env.Files();
        env.Applier.Apply(env.Plan(BlobB));
        Assert.NotEqual(afterA, env.Files());

        env.Applier.Undo("install-1", env.Client);
        Assert.Equal(afterA, env.Files());

        env.Applier.Undo("install-1", env.Client);
        Assert.Equal(original, env.Files());
    }

    [Fact]
    public void Undo_OfAnInterruptedUndo_ReportsAlreadyRestored()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Put("WTF/Config.wtf", "SET realmList \"old\"\r\n");
        var before = env.Files();
        env.Applier.Apply(env.Plan());
        env.Put("WTF/Config.wtf", "SET realmList \"old\"\r\n"); // as if a previous undo had already restored this one

        var undo = env.Applier.Undo("install-1", env.Client);

        Assert.Equal(UndoFileStatus.AlreadyRestored, undo.Files.Single(f => f.RelativePath == "WTF/Config.wtf").Status);
        Assert.Equal(before, env.Files());
    }

    [Fact]
    public void Undo_RefusesATamperedBackupCopy()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        env.Applier.Apply(env.Plan());
        var backup = Directory.GetDirectories(Path.Combine(env.Backups, "install-1")).Single();
        File.WriteAllText(Path.Combine(backup, "files", "0"), "tampered");
        var current = env.Files();

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.Equal(current, env.Files());
    }

    [Fact]
    public void Apply_WritesManifestBeforeClientChanges_AndDescribesTheBackup()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        var plan = env.Plan();
        var firstFileWritten = false;
        string? manifestAtFirstWrite = null;
        env.Writer.Fail = _ =>
        {
            if (!firstFileWritten)
            {
                firstFileWritten = true;
                var dir = Directory.GetDirectories(Path.Combine(env.Backups, "install-1")).Single();
                manifestAtFirstWrite = File.ReadAllText(Path.Combine(dir, "manifest.json"));
            }

            return false;
        };

        var result = env.Applier.Apply(plan);

        Assert.NotNull(manifestAtFirstWrite);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(env.Backups, "install-1", result.BackupId!, "manifest.json")));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(result.BackupId, root.GetProperty("backup_id").GetString());
        Assert.Equal("install-1", root.GetProperty("installation_id").GetString());
        Assert.Equal("profile-1", root.GetProperty("profile_id").GetString());
        Assert.Equal("12340 (3.3.5.12340)", root.GetProperty("client_build").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("undone_at").ValueKind);
        var file = root.GetProperty("files").EnumerateArray().First(f => f.GetProperty("path").GetString() == "Data/enUS/realmlist.wtf");
        Assert.True(file.GetProperty("existed").GetBoolean());
        Assert.Equal(plan.Edits.First(e => e.RelativePath == "Data/enUS/realmlist.wtf").Sha256Before, file.GetProperty("sha256_before").GetString());

        env.Applier.Undo("install-1", env.Client);
        using var undone = JsonDocument.Parse(File.ReadAllText(Path.Combine(env.Backups, "install-1", result.BackupId!, "manifest.json")));
        Assert.NotEqual(JsonValueKind.Null, undone.RootElement.GetProperty("undone_at").ValueKind);
    }

    [Fact]
    public void Apply_AndUndo_WithUnsafePathsOrIds_Reject()
    {
        using var env = new Env();
        var plan = env.Plan();
        var evil = plan with { Edits = [plan.Edits[0] with { RelativePath = "../outside.txt" }] };

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, Assert.Throws<ClientFilesException>(() => env.Applier.Apply(evil)).Code);
        Assert.Throws<ArgumentException>(() => env.Applier.Apply(plan with { InstallationId = "../x" }));
        Assert.Throws<ArgumentException>(() => env.Applier.Undo("..", env.Client));
    }

    [Fact]
    public void Apply_AndUndo_ForDifferentInstallations_AreIndependent()
    {
        using var env = new Env();
        env.Applier.Apply(env.Plan());

        Assert.Equal(ClientFilesErrorCode.NothingToUndo, Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-2", env.Client)).Code);
    }

    [Fact]
    public void Apply_AndUndo_WhileAnotherOperationHoldsTheInstallationLock_FailWithoutChanges()
    {
        using var env = new Env();
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        var plan = env.Plan();
        var before = env.Files();
        Directory.CreateDirectory(Path.Combine(env.Backups, "install-1"));

        using (new FileStream(Path.Combine(env.Backups, "install-1", ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<ClientFilesException>(() => env.Applier.Apply(plan));
            Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));
        }

        Assert.Equal(before, env.Files());
        Assert.NotNull(env.Applier.Apply(plan).BackupId); // the lock is released afterwards
    }

    [Fact]
    public void Undo_WithAnIncompleteManifest_FailsWithVerifyFailed()
    {
        using var env = new Env();
        env.Applier.Apply(env.Plan());
        var manifest = Directory.GetFiles(Path.Combine(env.Backups, "install-1"), "manifest.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"sha256_after\"", "\"sha256_afterx\""));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client)).Code);
    }

    private static string[] BackupIds(Env env) =>
        Directory.GetDirectories(Path.Combine(env.Backups, "install-1")).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Apply_KeepsOnlyTheNewestTenBackups_ByDefault()
    {
        using var env = new Env();
        var ids = new List<string>();
        for (var i = 0; i < 13; i++)
            ids.Add(env.Applier.Apply(env.Plan(i % 2 == 0 ? BlobA : BlobB)).BackupId!);

        Assert.Equal(ids.Skip(3).Order(StringComparer.Ordinal), BackupIds(env));
        Assert.Equal(ClientFilesApplier.DefaultKeepBackups, 10);
    }

    [Fact]
    public void Retention_IsConfigurable_AndUndoStillWorksOnTheNewestBackup()
    {
        using var env = new Env();
        var applier = new ClientFilesApplier(new ClientFilesPlanner(), env.Writer, env.Probe, env.Backups, keepBackups: 2);
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        for (var i = 0; i < 4; i++)
            applier.Apply(env.Plan(i % 2 == 0 ? BlobA : BlobB));
        var afterThree = env.Files();

        Assert.Equal(2, BackupIds(env).Length);
        var undo = applier.Undo("install-1", env.Client);

        Assert.All(undo.Files, f => Assert.Equal(UndoFileStatus.Restored, f.Status));
        Assert.NotEqual(afterThree, env.Files());
    }

    [Fact]
    public void Retention_NeverDeletesThePendingBackup_AndIgnoresForeignFolders()
    {
        using var env = new Env();
        var applier = new ClientFilesApplier(new ClientFilesPlanner(), env.Writer, env.Probe, env.Backups, keepBackups: 1);
        applier.Apply(env.Plan(BlobA));
        var newest = applier.Apply(env.Plan(BlobB)).BackupId!;
        var foreign = Path.Combine(env.Backups, "install-1", "0000-not-a-backup");
        Directory.CreateDirectory(foreign);

        applier.Apply(env.Plan(BlobA));
        var ids = BackupIds(env);

        Assert.Contains("0000-not-a-backup", ids);
        Assert.DoesNotContain(newest, ids); // older and not pending: removed
        Assert.Equal(2, ids.Length); // the foreign folder and the newest backup
    }

    [Theory]
    [InlineData(0)] // same instant for every apply
    [InlineData(-5)] // the clock goes backwards
    public void BackupIds_FollowApplyOrder_EvenWithAStuckOrBackwardsClock_SoRetentionKeepsTheLatest(int stepSeconds)
    {
        using var env = new Env();
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var applier = new ClientFilesApplier(new ClientFilesPlanner(), env.Writer, env.Probe, env.Backups, () => now = now.AddSeconds(stepSeconds), keepBackups: 1);
        env.Put("Data/enUS/realmlist.wtf", "set realmlist old.example.org\r\n");
        string? last = null;
        for (var i = 0; i < 5; i++)
            last = applier.Apply(env.Plan(i % 2 == 0 ? BlobA : BlobB)).BackupId;

        Assert.Equal([last!], BackupIds(env));
        Assert.All(applier.Undo("install-1", env.Client).Files, f => Assert.Equal(UndoFileStatus.Restored, f.Status));
    }

    [Fact]
    public void Retention_ValueBelowOne_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new FakeProbe(), "/x", keepBackups: 0));

    [Fact]
    public void DefaultBackupRoot_HonorsTheOverride()
    {
        Assert.Equal("/x/y", ClientFilesApplier.DefaultBackupRoot(k => k == ClientFilesApplier.BackupDirectoryVariable ? "/x/y" : null));
        Assert.EndsWith(Path.Combine("realm-portal", "backups"), ClientFilesApplier.DefaultBackupRoot(_ => null));
    }

    // ---- property: any starting state is restored exactly ----

    private static readonly Gen<byte[]?> FileGen =
        Gen.OneOf(
            Gen.Constant<byte[]?>(null),
            Gen.Elements<byte>(
                    (byte)'S', (byte)'E', (byte)'T', (byte)' ', (byte)'"', (byte)'\r', (byte)'\n', (byte)'x', 0xEF, 0xBB, 0xBF, 0xE9, 0x00, 0xFF)
                .ArrayOf()
                .Select(b => (byte[]?)b));

    [Property(MaxTest = 150)]
    public Property Undo_AfterApply_IsByteIdentical_ForAnyStartingFiles() =>
        Prop.ForAll(FileGen.ToArbitrary(), FileGen.ToArbitrary(), Gen.Elements(BlobA, BlobB).ToArbitrary(), (realmlist, config, blob) =>
        {
            using var env = new Env();
            if (realmlist is not null)
                env.Put("Data/enUS/realmlist.wtf", realmlist);
            if (config is not null)
                env.Put("WTF/Config.wtf", config);
            var before = env.Files();

            env.Applier.Apply(env.Plan(blob, "Realm"));
            env.Applier.Undo("install-1", env.Client);

            return before.OrderBy(p => p.Key).SequenceEqual(env.Files().OrderBy(p => p.Key));
        });
}
