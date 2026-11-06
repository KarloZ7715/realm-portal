using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;
using Box = RealmPortal.Tests.ConfigBackupTests.Box;

namespace RealmPortal.Tests;

/// <summary>Issue #196 review: unchanged saves, a broken .bak path, and a restore that fails or is interrupted.</summary>
public sealed class ConfigBackupSafetyTests
{
    private const string Damaged = "{ not json";

    /// <summary>Real atomic writes, except that writes to <see cref="FailOn"/> throw before touching the file.</summary>
    private sealed class FailingWriter : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();

        public string? FailOn { get; set; }

        public List<string> Written { get; } = [];

        public void WriteAllBytes(string path, byte[] content) => WriteAllBytes(path, content, null);

        public void WriteAllBytes(string path, byte[] content, FileReplacementPermissions? permissions)
        {
            if (FailOn is not null && Path.GetFullPath(path) == Path.GetFullPath(FailOn))
                throw new IOException("Injected write failure.");
            Written.Add(path);
            _inner.WriteAllBytes(path, content, permissions);
        }
    }

    [Fact]
    public void UnchangedServerSave_KeepsTheRealPreviousBak()
    {
        using var box = new Box();
        Assert.Equal(0, box.Cli("add", "Alpha", "alpha.example.test").Code);
        var afterAlpha = File.ReadAllBytes(box.ServersPath);
        Assert.Equal(0, box.Cli("add", "Beta", "beta.example.test").Code);
        var afterBeta = File.ReadAllBytes(box.ServersPath);

        // Same values: the save changes nothing, so neither servers.json nor its .bak is rewritten.
        Assert.Equal(0, box.Cli("edit", "Beta", "--host", "beta.example.test").Code);

        Assert.Equal(afterBeta, File.ReadAllBytes(box.ServersPath));
        Assert.Equal(afterAlpha, File.ReadAllBytes(box.ServersPath + ".bak"));
    }

    [Fact]
    public void UnchangedClientSave_KeepsTheRealPreviousBak()
    {
        using var box = new Box();
        var service = box.Service();
        var client = service.AddClient(box.NewClient("client-a")).Value!;
        var afterAdd = File.ReadAllBytes(box.ClientsPath);
        Assert.True(service.UpdateClient(new UpdateClientRequest(client.Id, Name: "Wrath")).Succeeded);
        var afterRename = File.ReadAllBytes(box.ClientsPath);

        Assert.True(service.UpdateClient(new UpdateClientRequest(client.Id, Name: "Wrath")).Succeeded);

        Assert.Equal(afterRename, File.ReadAllBytes(box.ClientsPath));
        Assert.Equal(afterAdd, File.ReadAllBytes(box.ClientsPath + ".bak"));
    }

    [Fact]
    public void WriteKeepingPrevious_WithTheSameBytes_WritesNothing()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("store.json");
        File.WriteAllText(path + ".bak", "older");
        File.WriteAllText(path, "same");
        var writer = new FailingWriter();

        var written = ConfigFileBackup.WriteKeepingPrevious(path, writer, "same"u8.ToArray(), null, _ => true);

        Assert.False(written);
        Assert.Empty(writer.Written);
        Assert.Equal("older", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void BakPathThatIsADirectory_DoesNotBlockTheSave_AndIsReportedDamaged()
    {
        using var box = new Box();
        var service = box.Service();
        Assert.True(service.AddServer(new NewServer("First", "first.example.test")).Succeeded);
        Directory.CreateDirectory(box.ServersPath + ".bak");

        Assert.True(service.AddServer(new NewServer("Second", "second.example.test")).Succeeded);

        Assert.Equal(2, service.ListServers().Value!.Count);
        var status = service.GetConfigBackups().Value!.Single(s => s.File == ConfigFileKind.Servers);
        Assert.Equal(ConfigFileState.Valid, status.Current);
        Assert.Equal(ConfigFileState.Damaged, status.Backup);
        var restored = service.RestoreConfigBackup(ConfigFileKind.Servers);
        Assert.Equal(ServiceErrorCode.StoreCorrupt, restored.Error!.Code);
        Assert.Equal(box.ServersPath + ".bak", restored.Error.Path);
    }

    [Fact]
    public void Restore_WhenTheWriteFails_LeavesTheOriginalAndNoBeforeRestore()
    {
        using var box = new Box();
        var writer = new FailingWriter();
        var service = box.Service(writer);
        service.AddServer(new NewServer("First", "first.example.test"));
        service.AddServer(new NewServer("Second", "second.example.test"));
        File.WriteAllText(box.ServersPath, Damaged);
        writer.FailOn = box.ServersPath;

        var restored = service.RestoreConfigBackup(ConfigFileKind.Servers);

        Assert.False(restored.Succeeded);
        Assert.Equal(Damaged, File.ReadAllText(box.ServersPath));
        Assert.False(File.Exists(box.ServersPath + ".before-restore"));
    }

    [NonPrivilegedLinuxFact]
    public void Restore_OfAnUnreadableFile_WhenTheWriteFails_PutsTheOriginalBack()
    {
        using var box = new Box();
        var writer = new FailingWriter();
        var service = box.Service(writer);
        service.AddServer(new NewServer("First", "first.example.test"));
        service.AddServer(new NewServer("Second", "second.example.test"));
        var original = File.ReadAllBytes(box.ServersPath);
        File.SetUnixFileMode(box.ServersPath, UnixFileMode.None);
        writer.FailOn = box.ServersPath;

        var restored = service.RestoreConfigBackup(ConfigFileKind.Servers);

        Assert.False(restored.Succeeded);
        Assert.True(File.Exists(box.ServersPath));
        Assert.False(File.Exists(box.ServersPath + ".before-restore"));
        File.SetUnixFileMode(box.ServersPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(original, File.ReadAllBytes(box.ServersPath));
    }

    [Fact]
    public void Restore_CopiesTheCurrentFileAside_AndNeverLeavesTheStoreMissing()
    {
        using var box = new Box();
        var writer = new FailingWriter();
        var service = box.Service(writer);
        service.AddServer(new NewServer("First", "first.example.test"));
        service.AddServer(new NewServer("Second", "second.example.test"));
        File.WriteAllText(box.ServersPath, Damaged);
        // The copy aside is written first; stopping right after it must leave servers.json in place.
        writer.FailOn = box.ServersPath;
        service.RestoreConfigBackup(ConfigFileKind.Servers);
        Assert.Contains(box.ServersPath + ".before-restore", writer.Written);
        Assert.True(File.Exists(box.ServersPath));
    }

    [UnixFact]
    public void BeforeRestore_IsPrivate_EvenWhenTheReplacedFileWasNot()
    {
        using var box = new Box();
        var service = box.Service();
        service.AddServer(new NewServer("First", "first.example.test"));
        service.AddServer(new NewServer("Second", "second.example.test"));
        File.SetUnixFileMode(box.ServersPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        Assert.True(service.RestoreConfigBackup(ConfigFileKind.Servers).Succeeded);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(box.ServersPath + ".before-restore"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(box.ServersPath));
    }

    [Fact]
    public void SecondRestore_KeepsTheFirstBeforeRestore()
    {
        using var box = new Box();
        var service = box.Service();
        service.AddServer(new NewServer("First", "first.example.test"));
        service.AddServer(new NewServer("Second", "second.example.test"));
        File.WriteAllText(box.ServersPath, Damaged);
        Assert.True(service.RestoreConfigBackup(ConfigFileKind.Servers).Succeeded);

        Assert.True(service.RestoreConfigBackup(ConfigFileKind.Servers).Succeeded);

        Assert.Equal(Damaged, File.ReadAllText(box.ServersPath + ".before-restore"));
    }

    [Fact]
    public void RestoreConfigBackup_LeavesStateJsonByteIdentical()
    {
        using var box = new Box();
        var service = box.Service();
        var client = service.AddClient(box.NewClient("client-a")).Value!;
        service.AddServer(new NewServer("First", "first.example.test"));
        var second = service.AddServer(new NewServer("Second", "second.example.test")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(client.Id, second.Id)).Succeeded);
        Assert.True(File.Exists(box.StatePath));
        var state = File.ReadAllBytes(box.StatePath);

        Assert.True(service.RestoreConfigBackup(ConfigFileKind.Servers).Succeeded);

        Assert.True(File.Exists(box.StatePath));
        Assert.Equal(state, File.ReadAllBytes(box.StatePath));
        Assert.Null(service.GetStatus().Value!.Active);
    }

    [Fact]
    public void GetConfigBackups_DoesNotCreateAMissingConfigDirectory()
    {
        using var box = new Box();

        var statuses = box.Service().GetConfigBackups();

        Assert.True(statuses.Succeeded);
        Assert.All(statuses.Value!, s => Assert.Equal(ConfigFileState.Missing, s.Current));
        Assert.False(Directory.Exists(box.Config));
    }

    [Fact]
    public void Cli_DoesNotSuggestRestoringADamagedBak()
    {
        using var box = new Box();
        Directory.CreateDirectory(box.Config);
        File.WriteAllText(box.ServersPath, Damaged);
        File.WriteAllText(box.ServersPath + ".bak", "also damaged");

        var list = box.Cli("list");

        Assert.Equal(2, list.Code);
        Assert.Contains("servers.json", list.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("config restore", list.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_ConfigBackups_ShowsTheTimeZoneOfTheBackupTime()
    {
        using var box = new Box();
        box.Cli("add", "First", "first.example.test");
        box.Cli("add", "Second", "second.example.test");

        var backups = box.Cli("config", "backups");

        Assert.Matches(@"servers\.json: current valid, backup valid \(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC[+-]\d{2}:\d{2}\)", backups.Out);
    }
}
