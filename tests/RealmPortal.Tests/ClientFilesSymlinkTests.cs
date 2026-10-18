using System.Diagnostics;
using System.Text.Json;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientFilesSymlinkTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private const string Original = "set realmlist shared.example.org\r\n";
    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    [Fact]
    public void Undo_RestoresSymlinkedRealmlist()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var shared = Path.Combine(client, "shared-realmlist.wtf");
        File.WriteAllText(shared, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, shared);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));

        Assert.Null(File.ResolveLinkTarget(linkPath, returnFinalTarget: true));
        Assert.Equal(BlobA, File.ReadAllText(linkPath));
        Assert.Equal(Original, File.ReadAllText(shared));
        var sidecar = Path.Combine(backups, "install-1", result.BackupId!, "files", "0.symlink");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(backups, "install-1", result.BackupId!, "manifest.json"))))
        {
            var entry = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString() == "Data/enUS/realmlist.wtf");
            Assert.Equal(ClientFilesIo.Sha256Hex(File.ReadAllBytes(sidecar)), entry.GetProperty("symlink_sha256").GetString());
        }

        applier.Undo("install-1", client);

        Assert.Equal(Path.GetFullPath(shared), File.ResolveLinkTarget(linkPath, returnFinalTarget: true)?.FullName);
        Assert.Equal(Original, File.ReadAllText(linkPath));
    }

    [Fact]
    public void Undo_PreservesRelativeSymlinkText_FromSidecar()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllText(Path.Combine(client, "shared-realmlist.wtf"), Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, "../../shared-realmlist.wtf");

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        applier.Undo("install-1", client);

        Assert.Equal("../../shared-realmlist.wtf", new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
    }

    [Fact]
    public void Undo_RejectsTamperedSymlinkSidecar_OutsideClient()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var shared = Path.Combine(client, "shared-realmlist.wtf");
        File.WriteAllText(shared, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, shared);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var sidecar = Path.Combine(backups, "install-1", result.BackupId!, "files", "0.symlink");
        File.WriteAllText(sidecar, "../../../etc/passwd");

        var ex = Assert.Throws<ClientFilesException>(() => applier.Undo("install-1", client));
        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.Null(File.ResolveLinkTarget(linkPath, returnFinalTarget: false));
        Assert.Equal(BlobA, File.ReadAllText(linkPath));
        Assert.Equal(Original, File.ReadAllText(shared));
    }

    [Fact]
    public void Undo_FallsBackToRegularFile_WhenSymlinkVerifyFails()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var good = Path.Combine(client, "good.wtf");
        File.WriteAllText(good, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, good);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        // The sidecar hash still matches. The target's bytes no longer do, so the recreated link is discarded.
        File.WriteAllText(good, "tampered after apply\r\n");

        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
        Assert.Equal("tampered after apply\r\n", File.ReadAllText(good));
    }

    [Fact]
    public void Undo_AbsoluteLinkOutsideClient_RestoresRegularFile()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var outside = temp.Combine("outside.wtf");
        File.WriteAllText(outside, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, outside);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
        Assert.Equal(Original, File.ReadAllText(outside));
    }

    [Fact]
    public void Undo_RelativeLinkEscapingClient_RestoresRegularFile()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var outside = temp.Combine("outside.wtf");
        File.WriteAllText(outside, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, "../../../outside.wtf");

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
        Assert.Equal(Original, File.ReadAllText(outside));
    }

    [Fact]
    public void Undo_TwoHopChainLeavingClient_RestoresRegularFile()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var outside = temp.Combine("outside.wtf");
        File.WriteAllText(outside, Original);
        var hop = Path.Combine(client, "hop");
        File.CreateSymbolicLink(hop, outside);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, "../../hop");

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
        Assert.Equal(outside, new FileInfo(hop).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(outside));
    }

    [Fact]
    public void Undo_SidecarWithoutSymlinkSha256_IsHashMismatch()
    {
        if (!SymlinksSupported())
            return;

        using var env = LinkedRealmlist();
        var switched = File.ReadAllText(env.LinkPath);
        EditManifest(env.ManifestPath, node =>
        {
            foreach (var file in node["files"]!.AsArray())
            {
                if (file!["path"]!.GetValue<string>() == "Data/enUS/realmlist.wtf")
                    file.AsObject().Remove("symlink_sha256");
            }
        });

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.Equal(switched, File.ReadAllText(env.LinkPath));
        Assert.Null(new FileInfo(env.LinkPath).LinkTarget);
    }

    [Fact]
    public void Undo_SidecarThatIsItselfASymlink_IsVerifyFailed()
    {
        if (!SymlinksSupported())
            return;

        using var env = LinkedRealmlist();
        var switched = File.ReadAllText(env.LinkPath);
        var decoy = env.Temp.Combine("decoy.txt");
        File.WriteAllText(decoy, "decoy");
        File.Delete(env.Sidecar);
        File.CreateSymbolicLink(env.Sidecar, decoy);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal(switched, File.ReadAllText(env.LinkPath));
        Assert.Equal("decoy", File.ReadAllText(decoy));
        Assert.Null(new FileInfo(env.LinkPath).LinkTarget);
    }

    [Fact]
    public void Undo_SidecarReplacedByFifo_IsVerifyFailed()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var env = LinkedRealmlist();
        var switched = File.ReadAllText(env.LinkPath);
        File.Delete(env.Sidecar);
        if (!TryCreateFifo(env.Sidecar))
            return;

        var started = Stopwatch.StartNew();
        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));
        started.Stop();

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal("The symlink sidecar is not a regular file.", ex.Message);
        Assert.InRange(started.ElapsedMilliseconds, 0, 5000);
        Assert.Equal(switched, File.ReadAllText(env.LinkPath));
        Assert.Null(new FileInfo(env.LinkPath).LinkTarget);
    }

    [Fact]
    public void Undo_OversizedSidecar_IsVerifyFailed()
    {
        if (!SymlinksSupported())
            return;

        using var env = LinkedRealmlist();
        var switched = File.ReadAllText(env.LinkPath);
        File.WriteAllText(env.Sidecar, new string('a', SymlinkSnapshot.MaxSidecarBytes + 1));

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal(switched, File.ReadAllText(env.LinkPath));
        Assert.Null(new FileInfo(env.LinkPath).LinkTarget);
    }

    [Fact]
    public void Undo_BadSidecarOnLaterEntry_RestoresNothing()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        var realmTarget = Path.Combine(client, "shared-realmlist.wtf");
        var configTarget = Path.Combine(client, "shared-config.wtf");
        File.WriteAllText(realmTarget, Original);
        File.WriteAllText(configTarget, "SET realmList \"old.example.org\"\r\n");
        var realmPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        var configPath = Path.Combine(client, "WTF", "Config.wtf");
        File.CreateSymbolicLink(realmPath, realmTarget);
        File.CreateSymbolicLink(configPath, configTarget);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var switchedRealm = File.ReadAllText(realmPath);
        var switchedConfig = File.ReadAllText(configPath);
        var manifestPath = Path.Combine(backups, "install-1", result.BackupId!, "manifest.json");
        var configBackup = BackupFileOf(manifestPath, "WTF/Config.wtf");
        File.WriteAllText(Path.Combine(backups, "install-1", result.BackupId!, configBackup + ".symlink"), "tampered-link-text");

        var ex = Assert.Throws<ClientFilesException>(() => applier.Undo("install-1", client));

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.Equal(switchedRealm, File.ReadAllText(realmPath));
        Assert.Equal(switchedConfig, File.ReadAllText(configPath));
        Assert.Null(new FileInfo(realmPath).LinkTarget);
        Assert.Null(new FileInfo(configPath).LinkTarget);
    }

    [Fact]
    public void Undo_TwoHopChainInsideClient_RestoresSymlink()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var shared = Path.Combine(client, "shared-realmlist.wtf");
        File.WriteAllText(shared, Original);
        var hop = Path.Combine(client, "hop");
        File.CreateSymbolicLink(hop, "shared-realmlist.wtf");
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, "../../hop");

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Equal("../../hop", new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
        Assert.Equal("shared-realmlist.wtf", new FileInfo(hop).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(shared));
    }

    [Fact]
    public void Undo_ManagedParentFolderSymlinkOutside_DoesNotWriteOutside()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var outside = temp.Combine("outside");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        Directory.CreateDirectory(Path.Combine(outside, "enUS"));
        var realmPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.WriteAllText(realmPath, Original);
        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var applied = File.ReadAllBytes(realmPath);

        Directory.Move(Path.Combine(client, "Data"), Path.Combine(client, "Data.real"));
        var outsideRealm = Path.Combine(outside, "enUS", "realmlist.wtf");
        File.WriteAllBytes(outsideRealm, applied);
        Directory.CreateSymbolicLink(Path.Combine(client, "Data"), outside);
        var outsideBefore = File.ReadAllBytes(outsideRealm);

        var ex = Assert.Throws<ClientFilesException>(() => applier.Undo("install-1", client));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal(outsideBefore, File.ReadAllBytes(outsideRealm));
        Assert.Equal(applied, File.ReadAllBytes(Path.Combine(client, "Data.real", "enUS", "realmlist.wtf")));
    }

    [Fact]
    public void Undo_DanglingLink_RestoresRegularFile()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var shared = Path.Combine(client, "shared-realmlist.wtf");
        File.WriteAllText(shared, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, "../../shared-realmlist.wtf");

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        File.Delete(shared);

        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
        Assert.False(File.Exists(shared));
    }

    [Fact]
    public void Undo_MissingSidecarWithSymlinkSha256_IsHashMismatch()
    {
        if (!SymlinksSupported())
            return;

        using var env = LinkedRealmlist();
        var switched = File.ReadAllText(env.LinkPath);
        File.Delete(env.Sidecar);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.Equal(switched, File.ReadAllText(env.LinkPath));
        Assert.Null(new FileInfo(env.LinkPath).LinkTarget);
    }

    [Fact]
    public void Undo_SidecarInvalidUtf8_IsVerifyFailed()
    {
        if (!SymlinksSupported())
            return;

        using var env = LinkedRealmlist();
        var switched = File.ReadAllText(env.LinkPath);
        File.WriteAllBytes(env.Sidecar, [0x80]);

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Contains("UTF-8", ex.Message, StringComparison.Ordinal);
        Assert.Equal(switched, File.ReadAllText(env.LinkPath));
        Assert.Null(new FileInfo(env.LinkPath).LinkTarget);
    }

    [Fact]
    public void Undo_SidecarControlCharacter_IsVerifyFailed()
    {
        if (!SymlinksSupported())
            return;

        using var env = LinkedRealmlist();
        var switched = File.ReadAllText(env.LinkPath);
        File.WriteAllBytes(env.Sidecar, "link\u0001text"u8.ToArray());

        var ex = Assert.Throws<ClientFilesException>(() => env.Applier.Undo("install-1", env.Client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Contains("control character", ex.Message, StringComparison.Ordinal);
        Assert.Equal(switched, File.ReadAllText(env.LinkPath));
        Assert.Null(new FileInfo(env.LinkPath).LinkTarget);
    }

    [Fact]
    public void Undo_AbsoluteLinkToClientPrefixLookalike_RestoresRegularFile()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var evilDir = temp.Combine("client-evil");
        Directory.CreateDirectory(evilDir);
        var evilFile = Path.Combine(evilDir, "x");
        File.WriteAllText(evilFile, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, evilFile);

        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var undo = applier.Undo("install-1", client);

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Null(new FileInfo(linkPath).LinkTarget);
        Assert.Equal(Original, File.ReadAllText(linkPath));
        Assert.Equal(Original, File.ReadAllText(evilFile));
    }

    [Fact]
    public void Undo_BackupFileReplacedBySymlink_IsVerifyFailed()
    {
        if (!SymlinksSupported())
            return;

        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var realmPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.WriteAllText(realmPath, Original);
        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var switched = File.ReadAllText(realmPath);
        var backupDir = Path.Combine(backups, "install-1", result.BackupId!);
        var backupFile = BackupFileOf(Path.Combine(backupDir, "manifest.json"), "Data/enUS/realmlist.wtf");
        var payloadPath = Path.Combine(backupDir, backupFile);
        var payload = File.ReadAllBytes(payloadPath);
        var decoy = temp.Combine("decoy-backup");
        File.WriteAllBytes(decoy, payload);
        File.Delete(payloadPath);
        File.CreateSymbolicLink(payloadPath, decoy);

        var ex = Assert.Throws<ClientFilesException>(() => applier.Undo("install-1", client));

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal("The backup copy is not a regular file.", ex.Message);
        Assert.Equal(switched, File.ReadAllText(realmPath));
        Assert.Equal(payload, File.ReadAllBytes(decoy));
    }

    [Fact]
    public async Task Undo_BackupFileReplacedByFifo_IsVerifyFailed()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var env = new AppliedRealm();
        var switched = File.ReadAllText(env.RealmPath);
        var payloadPath = PayloadPath(env, "Data/enUS/realmlist.wtf");
        File.Delete(payloadPath);
        if (!TryCreateFifo(payloadPath))
            return;

        // A FIFO with no writer blocks a following open. The timeout fails the test instead of hanging the runner.
        var undoTask = Task.Run(() => env.Applier.Undo("install-1", env.Client));
        var finished = await Task.WhenAny(undoTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == undoTask, "Undo hung while reading a FIFO backup.");
        var ex = await Assert.ThrowsAsync<ClientFilesException>(() => undoTask);

        Assert.Equal(ClientFilesErrorCode.VerifyFailed, ex.Code);
        Assert.Equal("The backup copy is not a regular file.", ex.Message);
        Assert.Equal(switched, File.ReadAllText(env.RealmPath));
    }

    [Fact]
    public async Task Undo_ManagedRealmlistReplacedByFifo_IsConflict()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var env = new AppliedRealm();
        File.Delete(env.RealmPath);
        if (!TryCreateFifo(env.RealmPath))
            return;

        var undoTask = Task.Run(() => env.Applier.Undo("install-1", env.Client));
        var finished = await Task.WhenAny(undoTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == undoTask, "Undo hung while reading a FIFO realmlist.");
        var undo = await undoTask;

        Assert.Equal(UndoFileStatus.Conflict, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Equal(0x1000, LinuxMode(env.RealmPath) & 0xF000);
    }

    [Fact]
    public async Task Undo_ManagedRealmlistSymlinkToDevZero_IsConflict()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var env = new AppliedRealm();
        File.Delete(env.RealmPath);
        File.CreateSymbolicLink(env.RealmPath, "/dev/zero");

        var undoTask = Task.Run(() => env.Applier.Undo("install-1", env.Client));
        var finished = await Task.WhenAny(undoTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == undoTask, "Undo hung while reading a symlink to /dev/zero.");
        var undo = await undoTask;

        Assert.Equal(UndoFileStatus.Conflict, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Equal("/dev/zero", new FileInfo(env.RealmPath).LinkTarget);
    }

    [Fact]
    public async Task Undo_RecreatedLinkTargetReplacedByFifo_RestoresRegularFileWithoutHanging()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var env = LinkedRealmlist();
        // The managed file was a symlink to <client>/shared-realmlist.wtf. Someone swaps that target for a
        // FIFO, which a following, unbounded read would block on while Undo holds the installation lock.
        var target = Path.Combine(env.Client, "shared-realmlist.wtf");
        File.Delete(target);
        if (!TryCreateFifo(target))
            return;

        var undoTask = Task.Run(() => env.Applier.Undo("install-1", env.Client));
        var finished = await Task.WhenAny(undoTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == undoTask, "Undo hung while reading through a recreated link to a FIFO.");
        var undo = await undoTask;

        Assert.Equal(UndoFileStatus.Restored, undo.Files.Single(f => f.RelativePath == "Data/enUS/realmlist.wtf").Status);
        Assert.Equal(0x8000, LinuxMode(env.LinkPath) & 0xF000);
        Assert.Equal(Original, File.ReadAllText(env.LinkPath));
        Assert.Equal(0x1000, LinuxMode(target) & 0xF000);
    }

    private static string PayloadPath(AppliedRealm env, string relativePath)
    {
        var backupFile = BackupFileOf(Path.Combine(env.BackupDir, "manifest.json"), relativePath);
        return Path.Combine(env.BackupDir, backupFile);
    }

    /// <summary><c>stat -c %f</c> is the hex <c>st_mode</c>. <c>S_IFMT</c> is <c>0xF000</c>; a FIFO is <c>0x1000</c>.</summary>
    private static int LinuxMode(string path)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "stat",
            ArgumentList = { "-c", "%f", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (process is null || !process.WaitForExit(5000))
            throw new InvalidOperationException($"stat did not finish for '{path}'.");
        return Convert.ToInt32(process.StandardOutput.ReadToEnd().Trim(), 16);
    }

    private static bool SymlinksSupported() => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private static bool TryCreateFifo(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "mkfifo",
                ArgumentList = { path },
            });
            if (process is null || !process.WaitForExit(5000))
            {
                try
                {
                    process?.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or IOException)
        {
            return false;
        }
    }

    private static LinkedEnv LinkedRealmlist()
    {
        var temp = new TempDirectory();
        var client = temp.Combine("client");
        var backups = temp.Combine("backups");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        var shared = Path.Combine(client, "shared-realmlist.wtf");
        File.WriteAllText(shared, Original);
        var linkPath = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        File.CreateSymbolicLink(linkPath, shared);
        var applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
        var result = applier.Apply(applier.Plan(new ClientFilesRequest("install-1", "profile-1", client, V3, BlobA, "")));
        var backupDir = Path.Combine(backups, "install-1", result.BackupId!);
        return new LinkedEnv(temp, client, linkPath, applier, Path.Combine(backupDir, "manifest.json"), Path.Combine(backupDir, "files", "0.symlink"));
    }

    private static string BackupFileOf(string manifestPath, string relativePath)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.GetProperty("files").EnumerateArray()
            .Single(f => f.GetProperty("path").GetString() == relativePath)
            .GetProperty("backup_file").GetString()!;
    }

    private static void EditManifest(string manifestPath, Action<System.Text.Json.Nodes.JsonNode> edit)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
        edit(node);
        File.WriteAllText(manifestPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class AppliedRealm : IDisposable
    {
        public AppliedRealm()
        {
            Temp = new TempDirectory();
            Client = Temp.Combine("client");
            var backups = Temp.Combine("backups");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            RealmPath = Path.Combine(Client, "Data", "enUS", "realmlist.wtf");
            File.WriteAllText(RealmPath, Original);
            Applier = new ClientFilesApplier(new ClientFilesPlanner(), new AtomicFileWriter(), new NeverRunning(), backups);
            var result = Applier.Apply(Applier.Plan(new ClientFilesRequest("install-1", "profile-1", Client, V3, BlobA, "")));
            BackupDir = Path.Combine(backups, "install-1", result.BackupId!);
        }

        public TempDirectory Temp { get; }
        public string Client { get; }
        public string RealmPath { get; }
        public string BackupDir { get; }
        public ClientFilesApplier Applier { get; }
        public void Dispose() => Temp.Dispose();
    }

    private sealed class LinkedEnv(TempDirectory temp, string client, string linkPath, ClientFilesApplier applier, string manifestPath, string sidecar) : IDisposable
    {
        public TempDirectory Temp { get; } = temp;
        public string Client { get; } = client;
        public string LinkPath { get; } = linkPath;
        public ClientFilesApplier Applier { get; } = applier;
        public string ManifestPath { get; } = manifestPath;
        public string Sidecar { get; } = sidecar;
        public void Dispose() => Temp.Dispose();
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
