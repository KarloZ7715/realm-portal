using System.Text;
using FsCheck;
using FsCheck.Xunit;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>Regression tests for #93: editing a server keeps the fields the edit leaves null, and a rescan finds no new duplicate.</summary>
public sealed class ServerEditPreservesFieldsTests
{
    private const string Host = "realm.example.test";
    private const string PatchLine = "set patchlist patch.example.test";

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not expected.");
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Root = _temp.Combine("candidates");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(_temp.Combine("backups"));
            Client = Path.Combine(Root, "client");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Directory.CreateDirectory(Path.Combine(Client, "WTF"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            File.WriteAllText(Path.Combine(Client, "Data", "enUS", "realmlist.wtf"), $"set realmlist {Host}\r\n{PatchLine}\r\n", Encoding.Latin1);
            File.WriteAllText(Path.Combine(Client, "WTF", "Config.wtf"), "SET realmName \"Example Realm\"\nSET accountName \"player1\"\n");
            Config = _temp.Combine("config");
            Service = new PortalService(Config, _temp.Combine("projection"), new PeClientVersionReader(), new NoLaunch(),
                new NeverRunning(), _ => _temp.Combine("backups"),
                scanOptions: new ClientScanOptions { IncludeDefaultRoots = false, IncludeSystemRoots = false });
        }

        public string Config { get; }
        public string Root { get; }
        public string Client { get; }
        public PortalService Service { get; }

        public ScanOutcome Scan()
        {
            var result = Service.Scan(new ScanRequest(AdditionalRoots: [Root]));
            Assert.True(result.Succeeded, result.Error?.ToString());
            return result.Value!;
        }

        public Server Stored(string id) => new ServerStore(Config, new AtomicFileWriter()).Find(id)!;

        public void Dispose() => _temp.Dispose();
    }

    /// <summary>The request the App's edit form sends today: no build, patchlist or extra lines.</summary>
    private static UpdateServerRequest FormEdit(Server server, string name) =>
        new(server.Id, name, server.Host, server.Realm, Account: server.Account, ClientId: server.ClientId);

    [Fact]
    public void Edit_WithNullBuildAndExtraLines_KeepsStoredValues()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client).Value!;
        var added = env.Service.AddServer(new NewServer("Example", Host, "Example Realm", RequiredClientBuild: 12340,
            Account: "player1", ClientId: client.Id, ExtraRealmlistLines: [PatchLine])).Value!;
        var before = env.Stored(added.Id);

        var edited = env.Service.UpdateServer(FormEdit(before, "Renamed"));

        Assert.True(edited.Succeeded, edited.Error?.ToString());
        Assert.Equal(before with { Name = "Renamed" }, env.Stored(added.Id));
        Assert.Equal($"set realmlist {Host}\r\n{PatchLine}\r\n", env.Stored(added.Id).Realmlist);
    }

    [Fact]
    public void Edit_WithExplicitValues_ReplacesOrClearsThem()
    {
        using var env = new Env();
        var added = env.Service.AddServer(new NewServer("Example", Host, RequiredClientBuild: 12340, ExtraRealmlistLines: [PatchLine])).Value!;

        Assert.True(env.Service.UpdateServer(new UpdateServerRequest(added.Id, "Example", Host, RequiredClientBuild: 8606,
            PatchlistHost: "other.example.test")).Succeeded);
        Assert.Equal(8606, env.Stored(added.Id).RequiredClientBuild);
        Assert.Equal($"set realmlist {Host}\r\nset patchlist other.example.test\r\n", env.Stored(added.Id).Realmlist);

        Assert.True(env.Service.UpdateServer(new UpdateServerRequest(added.Id, "Example", Host, ExtraRealmlistLines: [])).Succeeded);
        Assert.Equal($"set realmlist {Host}\r\n", env.Stored(added.Id).Realmlist);
        Assert.Equal(8606, env.Stored(added.Id).RequiredClientBuild);
    }

    [Fact]
    public void ImportedServer_EditThenRescan_CreatesNoDuplicate_AndSwitchKeepsPatchlist()
    {
        using var env = new Env();
        var imported = Assert.Single(env.Scan().ImportedServers);
        var stored = env.Stored(imported.Id);
        Assert.Equal(12340, stored.RequiredClientBuild);
        Assert.Contains(PatchLine, stored.Realmlist);

        Assert.True(env.Service.UpdateServer(FormEdit(stored, "My realm")).Succeeded);
        var clientId = stored.ClientId!;
        Assert.True(env.Service.RemoveClient(clientId).Succeeded);
        var rescan = env.Scan();

        Assert.Empty(rescan.ImportedServers);
        var only = Assert.Single(env.Service.ListServers().Value!);
        Assert.Equal("My realm", only.Name);
        Assert.DoesNotContain(env.Service.ListServers().Value!, s => s.Name.EndsWith(" #2", StringComparison.Ordinal));

        File.WriteAllText(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf"), "set realmlist placeholder.example.test\r\n", Encoding.Latin1);
        var switched = env.Service.SwitchServer(new SwitchRequest(clientId, only.Id));
        Assert.True(switched.Succeeded, switched.Error?.ToString());
        Assert.Contains(PatchLine, File.ReadAllText(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1));
    }

    [Fact]
    public void Rescan_MatchesStoredServerWithNullBuild_InsteadOfImportingDuplicate()
    {
        using var env = new Env();
        var manual = env.Service.AddServer(new NewServer("Manual", Host, "Example Realm")).Value!;

        var scan = env.Scan();

        Assert.Empty(scan.ImportedServers);
        var only = Assert.Single(env.Service.ListServers().Value!);
        Assert.Equal(manual.Id, only.Id);
        Assert.Null(env.Stored(manual.Id).RequiredClientBuild);
        Assert.Equal(scan.AddedClients.Single().Id, env.Stored(manual.Id).ClientId);
    }

    [Property(MaxTest = 200)]
    public void FindImportMatch_PrefersExactBuild_AndAcceptsNullBuild(PositiveInt build, bool exactFirst)
    {
        var any = Server.Create("Any", Host, "Example Realm");
        var exact = Server.Create("Exact", Host, "Example Realm", requiredClientBuild: build.Get);
        var other = Server.Create("Other", Host, "Example Realm", requiredClientBuild: build.Get + 1);
        var identity = RealmlistIdentity.Create(Host.ToUpperInvariant(), " example realm ", build.Get);

        Assert.Same(exact, PortalService.FindImportMatch(exactFirst ? [exact, any, other] : [other, any, exact], identity));
        Assert.Same(any, PortalService.FindImportMatch([other, any], identity));
        Assert.Null(PortalService.FindImportMatch([other], identity));
        Assert.Null(PortalService.FindImportMatch([any], RealmlistIdentity.Create(Host, "Other Realm", build.Get)));
    }
}
