using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public sealed class PortalServiceScanTests
{
    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not expected in a scan test.");
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    [Fact]
    public void Scan_RegistersNewClientsAndDeduplicatesServers_Idempotently_WithoutWritingCandidates()
    {
        using var temp = new TempDirectory();
        var config = temp.Combine("config");
        var projection = temp.Combine("projection");
        var backups = temp.Combine("backups");
        var root = temp.Combine("candidates");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(backups);
        var first = CreateClient(root, "first");
        var second = CreateClient(root, "second");
        var clientFilesBefore = new[] { Snapshot(first), Snapshot(second) };
        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups,
            scanOptions: new ClientScanOptions { IncludeDefaultRoots = false, IncludeSystemRoots = false });

        var progress = new List<ScanProgress>();
        var scanned = service.Scan(new ScanRequest(AdditionalRoots: [root]), new InlineProgress<ScanProgress>(progress.Add));

        Assert.True(scanned.Succeeded, scanned.Error?.ToString());
        Assert.Equal(2, scanned.Value!.AddedClients.Count);
        Assert.Single(scanned.Value.ImportedServers);
        Assert.Contains(progress, p => p.DirectoriesInspected > 0);
        Assert.Equal(clientFilesBefore, new[] { Snapshot(first), Snapshot(second) });
        var clientIds = scanned.Value.AddedClients.Select(c => c.Id).ToArray();
        var servers = service.ListServers();
        Assert.True(servers.Succeeded, servers.Error?.ToString());
        Assert.Single(servers.Value!);
        Assert.Equal(2, clientIds.Length);
        Assert.All(clientIds, id => Assert.True(service.ListServers(id).Value!.Single().IsActive));
        Assert.True(File.Exists(ActiveProjection.FilePath(projection)));

        var repeated = service.Scan(new ScanRequest(AdditionalRoots: [root]));

        Assert.True(repeated.Succeeded, repeated.Error?.ToString());
        Assert.Empty(repeated.Value!.AddedClients);
        Assert.Empty(repeated.Value.ImportedServers);
        Assert.Equal(2, repeated.Value.AllClients.Count);
        Assert.Single(service.ListServers().Value!);
    }

    [Fact]
    public void CliScan_UsesAdditionalRootAndPrintsProgressAndOutcome()
    {
        using var temp = new TempDirectory();
        var client = CreateClient(temp.Path, "client");
        var config = temp.Combine("config");
        var projection = temp.Combine("projection");
        var output = new StringWriter();
        var error = new StringWriter();
        var launcher = new FakeLauncher();
        var app = new CliApp(output, error, new PeClientVersionReader(), launcher,
            _ => temp.Combine("backups"), new ClientScanOptions { IncludeDefaultRoots = false, IncludeSystemRoots = false });
        var code = app.Run("scan", ["--root", temp.Path, "--config-dir", config, "--projection-dir", projection]);

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
        Assert.Contains("Scanning", output.ToString());
        Assert.Contains("Scan complete: 1 clients and 1 servers added.", output.ToString());
        Assert.Equal(0, launcher.Launches);
        Assert.True(File.Exists(Path.Combine(client, "Wow.exe")));
    }

    private static string CreateClient(string root, string name)
    {
        var client = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist logon.example.org\r\n", Encoding.Latin1);
        File.WriteAllText(Path.Combine(client, "WTF", "Config.wtf"), "SET realmName \"Icecrown\"\nSET accountName \"player1\"\n");
        return client;
    }

    private static IReadOnlyDictionary<string, byte[]> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);

    private sealed class FakeLauncher : IGameLauncher
    {
        public int Launches { get; private set; }
        public void Launch(string executablePath, string workingDirectory, string? wrapper) => Launches++;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
