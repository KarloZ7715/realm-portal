using System.Security.Cryptography;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>
/// <see cref="ClientFilesApplier.CanUndoWithoutConflict"/> (#213): read-only, and false instead of an exception when the
/// backup cannot be checked.
/// </summary>
public sealed class ClientFilesCanUndoWithoutConflictTests
{
    private const string InstallationId = "inst-1";
    private const string Original = "set realmlist original.example.test\r\n";
    private static readonly ClientVersion Wotlk = new(new Version(3, 3, 5, 12340));

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();

        public Box()
        {
            Client = _dir.Combine("client");
            Backups = _dir.Combine("backups");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllText(RealmlistPath, Original);
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunningGameProbe(), Backups);
        }

        public string Client { get; }
        public string Backups { get; }
        public string RealmlistPath => Path.Combine(Client, "Data", "enUS", "realmlist.wtf");
        public string ConfigPath => Path.Combine(Client, "WTF", "Config.wtf");
        public ClientFilesApplier Applier { get; }

        public string Apply(string host) => Applier.Apply(Applier.Plan(new ClientFilesRequest(
            InstallationId, "A", Client, Wotlk, $"set realmlist {host}\r\n", ""))).BackupId!;

        public bool CanUndo(string backupId, string? clientRoot = null) =>
            Applier.CanUndoWithoutConflict(InstallationId, clientRoot ?? Client, backupId);

        public string ManifestPath(string backupId) => Path.Combine(Backups, InstallationId, backupId, "manifest.json");

        public string Snapshot() => string.Join('\n', new[] { Client, Backups }
            .SelectMany(d => Directory.EnumerateFileSystemEntries(d, "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(p => File.Exists(p) ? p + "|" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) : p));

        public string Combine(string name) => _dir.Combine(name);

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void True_RightAfterTheApply_AndWritesNothing()
    {
        using var box = new Box();
        var id = box.Apply("a.example.test");
        var before = box.Snapshot();

        Assert.True(box.CanUndo(id));
        Assert.Equal(before, box.Snapshot());
    }

    [Fact]
    public void True_WhenTheFilesAreAlreadyBackToTheirOriginalBytes()
    {
        using var box = new Box();
        var id = box.Apply("a.example.test");
        File.WriteAllText(box.RealmlistPath, Original);

        Assert.True(box.CanUndo(id));
    }

    [Fact]
    public void False_WhenAFileHasOtherBytes()
    {
        using var box = new Box();
        var id = box.Apply("a.example.test");
        File.WriteAllText(box.RealmlistPath, "set realmlist other.example.test\r\n");

        Assert.False(box.CanUndo(id));
    }

    [Fact]
    public void False_WhenACreatedFileWasReplacedByOneThatCannotBeCompared()
    {
        // Config.wtf did not exist before the apply (no "before" hash). A file too large to compare must still count as
        // a conflict, not as "back to the original".
        using var box = new Box();
        var id = box.Apply("a.example.test");
        Assert.True(File.Exists(box.ConfigPath));
        using (var stream = new FileStream(box.ConfigPath, FileMode.Create, FileAccess.Write))
            stream.SetLength(ClientFilesApplier.MaxBackupFileBytes + 1L);

        Assert.False(box.CanUndo(id));
    }

    [Fact]
    public void False_NotAnException_WhenTheManifestIsCorrupt()
    {
        using var box = new Box();
        var id = box.Apply("a.example.test");
        File.WriteAllText(box.ManifestPath(id), "{broken");

        Assert.False(box.CanUndo(id));
    }

    [Fact]
    public void False_NotAnException_WhenAClientFileCannotBeRead()
    {
        using var box = new Box();
        var id = box.Apply("a.example.test");
        if (OperatingSystem.IsWindows())
        {
            // Windows enforces the share mode, so a stream without FileShare.Read makes the file unreadable.
            using var exclusive = new FileStream(box.RealmlistPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.False(box.CanUndo(id));
            return;
        }

        // Root reads a mode-000 file anyway, so the file cannot be made unreadable there.
        if (Environment.IsPrivilegedProcess)
            return;

        var mode = File.GetUnixFileMode(box.RealmlistPath);
        File.SetUnixFileMode(box.RealmlistPath, UnixFileMode.None);
        try
        {
            Assert.False(box.CanUndo(id));
        }
        finally
        {
            File.SetUnixFileMode(box.RealmlistPath, mode);
        }
    }

    [Fact]
    public void False_NotAnException_ForAnotherClientFolder()
    {
        using var box = new Box();
        var id = box.Apply("a.example.test");
        var other = box.Combine("other");
        Directory.CreateDirectory(other);

        Assert.False(box.CanUndo(id, other));
    }
}
