using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>Issue #196: servers.json and clients.json keep a .bak of their previous valid version, which can be restored.</summary>
public sealed class ConfigBackupTests
{
    private const string Damaged = "{ not json";

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string ServersPath => Path.Combine(Config, "servers.json");
        public string ClientsPath => Path.Combine(Config, "clients.json");

        public PortalService Service() =>
            new(Config, Projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunningGameProbe(), TestBackupDir.Variables(_dir));

        public string NewClient(string name)
        {
            var client = _dir.Combine(name);
            Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            return client;
        }

        public (int Code, string Out, string Err) Cli(params string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            string[] common = ["--config-dir", Config, "--projection-dir", Projection];
            var code = Program.Run([.. args, .. common], o, e, new PeClientVersionReader(), new FakeLauncher(),
                TestBackupDir.Variables(_dir), new NeverRunningGameProbe());
            return (code, o.ToString(), e.ToString());
        }

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void ServersJson_KeepsThePreviousValidVersionAsBak()
    {
        using var box = new Box();
        var service = box.Service();
        Assert.True(service.AddServer(new NewServer("First", "first.example.test")).Succeeded);
        // The first write has no previous version to keep.
        Assert.False(File.Exists(box.ServersPath + ".bak"));
        var afterFirst = File.ReadAllBytes(box.ServersPath);

        Assert.True(service.AddServer(new NewServer("Second", "second.example.test")).Succeeded);

        Assert.Equal(afterFirst, File.ReadAllBytes(box.ServersPath + ".bak"));
        var status = service.GetConfigBackups().Value!.Single(s => s.File == ConfigFileKind.Servers);
        Assert.Equal(ConfigFileState.Valid, status.Current);
        Assert.Equal(ConfigFileState.Valid, status.Backup);
        Assert.NotNull(status.BackupModifiedAt);
    }

    [Fact]
    public void ClientsJson_KeepsThePreviousValidVersionAsBak()
    {
        using var box = new Box();
        var service = box.Service();
        Assert.True(service.AddClient(box.NewClient("client-a")).Succeeded);
        var afterFirst = File.ReadAllBytes(box.ClientsPath);

        Assert.True(service.AddClient(box.NewClient("client-b")).Succeeded);

        Assert.Equal(afterFirst, File.ReadAllBytes(box.ClientsPath + ".bak"));
        var status = service.GetConfigBackups().Value!.Single(s => s.File == ConfigFileKind.Clients);
        Assert.Equal(ConfigFileState.Valid, status.Backup);
    }

    [Fact]
    public void Bak_HasTheSamePrivatePermissionsAsTheStoreFile()
    {
        if (OperatingSystem.IsWindows()) return;
        using var box = new Box();
        var service = box.Service();
        service.AddServer(new NewServer("First", "first.example.test"));
        service.AddServer(new NewServer("Second", "second.example.test"));
        service.AddClient(box.NewClient("client-a"));
        service.AddClient(box.NewClient("client-b"));

        const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        foreach (var path in new[] { box.ServersPath, box.ClientsPath })
        {
            Assert.Equal(Private, File.GetUnixFileMode(path));
            Assert.Equal(File.GetUnixFileMode(path), File.GetUnixFileMode(path + ".bak"));
        }
    }

    [Fact]
    public void DamagedServersJson_IsRecoveredFromTheBak_AndTheDamagedFileIsKept()
    {
        using var box = new Box();
        var service = box.Service();
        service.AddServer(new NewServer("First", "first.example.test"));
        service.AddServer(new NewServer("Second", "second.example.test"));
        File.WriteAllText(box.ServersPath, Damaged);

        var broken = service.ListServers();
        Assert.False(broken.Succeeded);
        Assert.Equal(ServiceErrorCode.StoreCorrupt, broken.Error!.Code);
        Assert.Equal(ConfigFileState.Damaged, service.GetConfigBackups().Value!.Single(s => s.File == ConfigFileKind.Servers).Current);

        var restored = service.RestoreConfigBackup(ConfigFileKind.Servers);

        Assert.True(restored.Succeeded);
        Assert.Equal(ConfigFileState.Valid, restored.Value!.Current);
        Assert.Equal("First", Assert.Single(service.ListServers().Value!).Name);
        Assert.Equal(Damaged, File.ReadAllText(box.ServersPath + ".before-restore"));
        Assert.True(File.Exists(box.ServersPath + ".bak"));
    }

    [Fact]
    public void DamagedMainAndDamagedBak_RestoreFailsAndChangesNothing()
    {
        using var box = new Box();
        Directory.CreateDirectory(box.Config);
        File.WriteAllText(box.ServersPath, Damaged, Encoding.UTF8);
        File.WriteAllText(box.ServersPath + ".bak", "also damaged", Encoding.UTF8);
        var service = box.Service();

        var restored = service.RestoreConfigBackup(ConfigFileKind.Servers);

        Assert.False(restored.Succeeded);
        Assert.Equal(ServiceErrorCode.StoreCorrupt, restored.Error!.Code);
        Assert.Equal(box.ServersPath + ".bak", restored.Error.Path);
        Assert.Equal(Damaged, File.ReadAllText(box.ServersPath));
        Assert.False(File.Exists(box.ServersPath + ".before-restore"));
    }

    [Fact]
    public void Restore_WithoutBak_IsBackupNotFound()
    {
        using var box = new Box();
        var service = box.Service();
        service.AddServer(new NewServer("Only", "only.example.test"));

        var restored = service.RestoreConfigBackup(ConfigFileKind.Clients);

        Assert.False(restored.Succeeded);
        Assert.Equal(ServiceErrorCode.BackupNotFound, restored.Error!.Code);
        var statuses = service.GetConfigBackups().Value!;
        Assert.Equal(ConfigFileState.Missing, statuses.Single(s => s.File == ConfigFileKind.Clients).Current);
        Assert.Equal(ConfigFileState.Missing, statuses.Single(s => s.File == ConfigFileKind.Servers).Backup);
    }

    [Fact]
    public void KeepPrevious_NeverReplacesAGoodBakWithADamagedFile()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("store.json");
        File.WriteAllText(path + ".bak", "good");
        File.WriteAllText(path, "bad");

        ConfigFileBackup.KeepPrevious(path, new AtomicFileWriter(), bytes => Encoding.UTF8.GetString(bytes) == "good");

        Assert.Equal("good", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Cli_ConfigBackups_And_Restore()
    {
        using var box = new Box();
        Assert.Equal(0, box.Cli("add", "First", "first.example.test").Code);
        Assert.Equal(0, box.Cli("add", "Second", "second.example.test").Code);
        File.WriteAllText(box.ServersPath, Damaged);

        var list = box.Cli("list");
        Assert.Equal(2, list.Code);
        Assert.Contains("realm-portal config restore servers", list.Err, StringComparison.Ordinal);

        var backups = box.Cli("config", "backups");
        Assert.Equal(0, backups.Code);
        Assert.Contains("servers.json: current damaged, backup valid", backups.Out, StringComparison.Ordinal);
        Assert.Contains("clients.json: current missing, backup missing", backups.Out, StringComparison.Ordinal);

        var restore = box.Cli("config", "restore", "servers");
        Assert.Equal(0, restore.Code);
        Assert.Contains("Restored servers.json from servers.json.bak", restore.Out, StringComparison.Ordinal);
        Assert.Contains("First", box.Cli("list", "--all").Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("config")]
    [InlineData("config", "restore")]
    [InlineData("config", "restore", "state")]
    [InlineData("config", "nope")]
    public void Cli_ConfigUsageErrors(params string[] args)
    {
        using var box = new Box();
        var result = box.Cli(args);
        Assert.Equal(1, result.Code);
        Assert.Contains("Usage:", result.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_RestoreWithoutBak_Fails()
    {
        using var box = new Box();
        var result = box.Cli("config", "restore", "clients");
        Assert.Equal(2, result.Code);
        Assert.Contains("not found", result.Err, StringComparison.OrdinalIgnoreCase);
    }
}
