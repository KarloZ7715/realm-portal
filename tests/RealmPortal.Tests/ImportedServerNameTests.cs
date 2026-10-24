using System.Text;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>#98: an imported server is named after its realm (or its host when there is none), never "realm (host)".</summary>
public sealed class ImportedServerNameTests
{
    private const string Host = "realm.example.test";

    private static ExtractedServer Extracted(string? realm, string host = Host) => new(host, realm, null, null, null, null);

    private static IReadOnlyList<Server> Existing(params string[] names) =>
        names.Select((name, i) => Server.Create(name, $"h{i}.example.test")).ToArray();

    [Theory]
    [InlineData("Example Realm", "Example Realm")]
    [InlineData("  Example Realm  ", "Example Realm")]
    [InlineData("Exa\u0007mple\tRealm", "ExampleRealm")]
    public void Realm_IsTheName_WithoutHost(string realm, string expected) =>
        Assert.Equal(expected, PortalService.CreateImportedServerName(Extracted(realm), []));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001\u0002")]
    public void NoRealm_UsesHostOnly(string? realm) =>
        Assert.Equal(Host, PortalService.CreateImportedServerName(Extracted(realm), []));

    [Fact]
    public void HostWithPort_IsKeptAsIs() =>
        Assert.Equal("realm.example.test:3725", PortalService.CreateImportedServerName(Extracted(null, "realm.example.test:3725"), []));

    [Fact]
    public void Collisions_GetNumberedSuffix_CaseInsensitively()
    {
        Assert.Equal("Example Realm #2", PortalService.CreateImportedServerName(Extracted("Example Realm"), Existing("example realm")));
        Assert.Equal("Example Realm #3",
            PortalService.CreateImportedServerName(Extracted("Example Realm"), Existing("Example Realm", "EXAMPLE REALM #2")));
        Assert.Equal($"{Host} #2", PortalService.CreateImportedServerName(Extracted(null), Existing(Host)));
    }

    [Fact]
    public void LongRealm_IsCappedAt64_IncludingSuffix()
    {
        var realm = new string('r', 80);
        var first = PortalService.CreateImportedServerName(Extracted(realm), []);
        Assert.Equal(new string('r', Server.MaxNameLength), first);

        var second = PortalService.CreateImportedServerName(Extracted(realm), Existing(first));
        Assert.Equal(new string('r', Server.MaxNameLength - 3) + " #2", second);
        Assert.Equal(Server.MaxNameLength, second.Length);
    }

    [Fact]
    public void Scan_NamesImportedServerAfterRealm_AndKeepsUserNamedServers()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("candidates");
        var client = Path.Combine(root, "client");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        Directory.CreateDirectory(temp.Combine("backups"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), $"set realmlist {Host}\r\n", Encoding.Latin1);
        File.WriteAllText(Path.Combine(client, "WTF", "Config.wtf"), "SET realmName \"Example Realm\"\n");
        var service = new PortalService(temp.Combine("config"), temp.Combine("projection"), new PeClientVersionReader(), new NoLaunch(),
            new NeverRunning(), _ => temp.Combine("backups"),
            scanOptions: new ClientScanOptions { IncludeDefaultRoots = false, IncludeSystemRoots = false });
        var mine = service.AddServer(new NewServer("Example Realm", "other.example.test")).Value!;

        var scan = service.Scan(new ScanRequest(AdditionalRoots: [root]));

        Assert.True(scan.Succeeded, scan.Error?.ToString());
        Assert.Equal("Example Realm #2", Assert.Single(scan.Value!.ImportedServers).Name);
        Assert.Equal("Example Realm", service.ListServers().Value!.Single(s => s.Id == mine.Id).Name);
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not expected.");
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
