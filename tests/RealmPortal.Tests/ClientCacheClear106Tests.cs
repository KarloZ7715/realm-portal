using System.Text;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>Issue #106: optional Cache/WDB clear when switching servers.</summary>
public sealed class ClientCacheClear106Tests
{
    private sealed class FakeLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Client = _temp.Combine("client");
            Outside = _temp.Combine("outside");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Projection);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Directory.CreateDirectory(Outside);
            Directory.CreateDirectory(Backups);
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Service = new PortalService(Config, Projection, new PeClientVersionReader(), new FakeLaunch(),
                new NeverRunningGameProbe(), _ => Backups);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Client { get; }
        public string Outside { get; }
        public string Backups { get; }
        public PortalService Service { get; }

        public string ClientId => Service.AddClient(Client).Value!.Id;

        public void PutRealmlist(string content) =>
            File.WriteAllBytes(Path.Combine(Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1.GetBytes(content));

        public void SeedCache()
        {
            var cache = Path.Combine(Client, "Cache");
            var wdb = Path.Combine(Client, "WDB");
            Directory.CreateDirectory(Path.Combine(cache, "WDB"));
            Directory.CreateDirectory(wdb);
            File.WriteAllText(Path.Combine(cache, "marker.wdb"), "cache-data");
            File.WriteAllText(Path.Combine(cache, "WDB", "nested.wdb"), "nested");
            File.WriteAllText(Path.Combine(wdb, "top.wdb"), "wdb-data");
            Directory.CreateDirectory(Path.Combine(Client, "WTF"));
            File.WriteAllText(Path.Combine(Client, "WTF", "Config.wtf"), "SET realmName \"Keep\"\r\n");
            File.WriteAllText(Path.Combine(Client, "Data", "enUS", "keep.txt"), "keep-me");
        }

        public void Dispose() => _temp.Dispose();
    }

    [Fact]
    public void Clearer_RemovesOnlyCacheAndWdb_LeavesOtherClientFiles()
    {
        using var env = new Env();
        env.SeedCache();

        ClientCacheClearer.Clear(env.Client);

        Assert.False(Directory.Exists(Path.Combine(env.Client, "Cache")));
        Assert.False(Directory.Exists(Path.Combine(env.Client, "WDB")));
        Assert.True(File.Exists(Path.Combine(env.Client, "WTF", "Config.wtf")));
        Assert.True(File.Exists(Path.Combine(env.Client, "Data", "enUS", "keep.txt")));
        Assert.True(File.Exists(Path.Combine(env.Client, "Wow.exe")));
    }

    [UnixFact]
    public void Clearer_EscapingCacheSymlink_RefusesAndLeavesOutsideIntact()
    {
        using var env = new Env();
        var outsideCache = Path.Combine(env.Outside, "Cache");
        Directory.CreateDirectory(outsideCache);
        File.WriteAllText(Path.Combine(outsideCache, "secret.wdb"), "SECRET");
        Directory.CreateSymbolicLink(Path.Combine(env.Client, "Cache"), outsideCache);

        var ex = Assert.Throws<ClientFilesException>(() => ClientCacheClearer.Clear(env.Client));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.Equal("SECRET", File.ReadAllText(Path.Combine(outsideCache, "secret.wdb")));
        Assert.True(Directory.Exists(Path.Combine(env.Client, "Cache")));
    }

    [UnixFact]
    public void Clearer_CacheSymlinkToWtfInsideClient_Refused_LeavesWtfIntact()
    {
        using var env = new Env();
        Directory.CreateDirectory(Path.Combine(env.Client, "WTF"));
        File.WriteAllText(Path.Combine(env.Client, "WTF", "Config.wtf"), "SET realmName \"Keep\"\r\n");
        Directory.CreateSymbolicLink(Path.Combine(env.Client, "Cache"), Path.Combine(env.Client, "WTF"));

        var ex = Assert.Throws<ClientFilesException>(() => ClientCacheClearer.Clear(env.Client));

        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
        Assert.True(File.Exists(Path.Combine(env.Client, "WTF", "Config.wtf")));
        Assert.Contains("Keep", File.ReadAllText(Path.Combine(env.Client, "WTF", "Config.wtf")));
    }

    [UnixFact]
    public void Clearer_SymlinkInsideCache_DeletesLinkOnly_NotOutsideTarget()
    {
        using var env = new Env();
        Directory.CreateDirectory(Path.Combine(env.Client, "Cache"));
        File.WriteAllText(Path.Combine(env.Client, "Cache", "local.wdb"), "local");
        File.WriteAllText(Path.Combine(env.Outside, "planted.wdb"), "OUTSIDE");
        File.CreateSymbolicLink(Path.Combine(env.Client, "Cache", "escape.wdb"), Path.Combine(env.Outside, "planted.wdb"));

        ClientCacheClearer.Clear(env.Client);

        Assert.False(Directory.Exists(Path.Combine(env.Client, "Cache")));
        Assert.Equal("OUTSIDE", File.ReadAllText(Path.Combine(env.Outside, "planted.wdb")));
    }

    [Fact]
    public void Switch_GlobalOff_LeavesCache()
    {
        using var env = new Env();
        env.SeedCache();
        env.PutRealmlist("set realmlist old.example.org\r\n");
        var clientId = env.ClientId;
        var a = env.Service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = env.Service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(env.Service.SetStorageSettings(new StorageSettings(env.Backups, ClearCacheOnSwitch: false)).Succeeded);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        var sw = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id));

        Assert.True(sw.Succeeded);
        Assert.Null(sw.Value!.CacheClearWarning);
        Assert.True(File.Exists(Path.Combine(env.Client, "Cache", "marker.wdb")));
        Assert.True(File.Exists(Path.Combine(env.Client, "WDB", "top.wdb")));
    }

    [Fact]
    public void Switch_GlobalOn_ClearsCacheAfterSuccessfulSwitch()
    {
        using var env = new Env();
        env.SeedCache();
        env.PutRealmlist("set realmlist old.example.org\r\n");
        var clientId = env.ClientId;
        var a = env.Service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = env.Service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(env.Service.SetStorageSettings(new StorageSettings(env.Backups, ClearCacheOnSwitch: true)).Succeeded);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        // First switch also clears when leaving "no active"/changing server with setting on.
        Assert.False(Directory.Exists(Path.Combine(env.Client, "Cache")));
        env.SeedCache();
        var sw = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id));

        Assert.True(sw.Succeeded);
        Assert.Null(sw.Value!.CacheClearWarning);
        Assert.False(Directory.Exists(Path.Combine(env.Client, "Cache")));
        Assert.False(Directory.Exists(Path.Combine(env.Client, "WDB")));
        Assert.True(File.Exists(Path.Combine(env.Client, "WTF", "Config.wtf")));
        Assert.Contains("b.example.org", File.ReadAllText(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1));
    }

    [Fact]
    public void Switch_PerServerOverrideTrue_BeatsGlobalOff()
    {
        using var env = new Env();
        env.SeedCache();
        env.PutRealmlist("set realmlist old.example.org\r\n");
        var clientId = env.ClientId;
        var a = env.Service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = env.Service.AddServer(new NewServer("B", "b.example.org", ClearCacheOnSwitch: true)).Value!;
        Assert.True(env.Service.SetStorageSettings(new StorageSettings(env.Backups, ClearCacheOnSwitch: false)).Succeeded);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.True(File.Exists(Path.Combine(env.Client, "Cache", "marker.wdb")));
        var sw = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id));

        Assert.True(sw.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(env.Client, "Cache")));
    }

    [Fact]
    public void Switch_PerServerOverrideFalse_BeatsGlobalOn()
    {
        using var env = new Env();
        env.SeedCache();
        env.PutRealmlist("set realmlist old.example.org\r\n");
        var clientId = env.ClientId;
        var a = env.Service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = env.Service.AddServer(new NewServer("B", "b.example.org", ClearCacheOnSwitch: false)).Value!;
        Assert.True(env.Service.SetStorageSettings(new StorageSettings(env.Backups, ClearCacheOnSwitch: true)).Succeeded);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        env.SeedCache();
        var sw = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id));

        Assert.True(sw.Succeeded);
        Assert.True(File.Exists(Path.Combine(env.Client, "Cache", "marker.wdb")));
    }

    [UnixFact]
    public void Switch_EscapingCache_FailSoft_SwitchStillSucceeds()
    {
        using var env = new Env();
        env.PutRealmlist("set realmlist old.example.org\r\n");
        var outsideCache = Path.Combine(env.Outside, "Cache");
        Directory.CreateDirectory(outsideCache);
        File.WriteAllText(Path.Combine(outsideCache, "secret.wdb"), "SECRET");
        Directory.CreateSymbolicLink(Path.Combine(env.Client, "Cache"), outsideCache);
        var clientId = env.ClientId;
        var a = env.Service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = env.Service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(env.Service.SetStorageSettings(new StorageSettings(env.Backups, ClearCacheOnSwitch: true)).Succeeded);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        var sw = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id));

        Assert.True(sw.Succeeded, sw.Error?.ToString());
        Assert.NotNull(sw.Value!.CacheClearWarning);
        Assert.Equal("SECRET", File.ReadAllText(Path.Combine(outsideCache, "secret.wdb")));
        Assert.Contains("b.example.org", File.ReadAllText(Path.Combine(env.Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1));
    }

    [Fact]
    public void StorageSettings_ClearCacheDefaultsOff_AndRoundTrips()
    {
        using var env = new Env();
        var got = env.Service.GetStorageSettings();
        Assert.True(got.Succeeded);
        Assert.False(got.Value!.ClearCacheOnSwitch);

        Assert.True(env.Service.SetStorageSettings(new StorageSettings(env.Backups, ClearCacheOnSwitch: true)).Succeeded);
        var again = env.Service.GetStorageSettings();
        Assert.True(again.Succeeded);
        Assert.True(again.Value!.ClearCacheOnSwitch);

        // Restart service against same config.
        var restarted = new PortalService(env.Config, env.Projection, new PeClientVersionReader(), new FakeLaunch(),
            new NeverRunningGameProbe(), _ => env.Backups);
        Assert.True(restarted.GetStorageSettings().Value!.ClearCacheOnSwitch);
    }

    [Fact]
    public void ServerStore_PersistsClearCacheOnSwitchOverride()
    {
        using var env = new Env();
        var added = env.Service.AddServer(new NewServer("A", "a.example.org", ClearCacheOnSwitch: true)).Value!;
        Assert.True(added.ClearCacheOnSwitch);

        var listed = env.Service.ListServers().Value!.Single();
        Assert.True(listed.ClearCacheOnSwitch);

        Assert.True(env.Service.UpdateServer(new UpdateServerRequest(added.Id, "A", "a.example.org", ClearCacheOnSwitch: false)).Succeeded);
        Assert.False(env.Service.ListServers().Value!.Single().ClearCacheOnSwitch);
    }
}
