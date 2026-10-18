using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public sealed class CrudServiceTests
{
    private sealed class Probe : IGameProcessProbe
    {
        public HashSet<string> RunningPaths { get; } = new(StringComparer.Ordinal);
        public bool IsGameRunning(string clientRoot) => RunningPaths.Contains(Path.GetFullPath(clientRoot));
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not used by these tests.");
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();
        private readonly Probe _probe = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Backup = _temp.Combine("backups");
            Client = MakeClient("client");
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            Service = new PortalService(Config, Projection, new PeClientVersionReader(), new NoLaunch(), _probe, _ => Backup);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Backup { get; }
        public string Client { get; }
        public PortalService Service { get; }
        public Probe GameProbe => _probe;

        public string MakeClient(string name)
        {
            var path = _temp.Combine(name);
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(path, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            return path;
        }

        public void Dispose() => _temp.Dispose();
    }

    [Fact]
    public void ClientCrud_PersistsNameOriginAndMigratesServerPinOnPathEdit()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client, "Wrath").Value!;
        var server = env.Service.AddServer(new NewServer("Alpha", "alpha.example.org", ClientId: client.Id)).Value!;
        File.WriteAllText(Path.Combine(env.Config, "state.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1,
            active_id = server.Id,
            undo = new { server_id = (string?)null },
            active_by_client = new Dictionary<string, string> { [client.Id] = server.Id },
            undo_by_client = new Dictionary<string, object> { [client.Id] = new { server_id = server.Id } },
        }));
        var replacementPath = env.MakeClient("replacement");

        var updated = env.Service.UpdateClient(new UpdateClientRequest(client.Id, "Wrath II", replacementPath));

        Assert.True(updated.Succeeded);
        Assert.NotEqual(client.Id, updated.Value!.Id);
        Assert.Equal(replacementPath, updated.Value.Path);
        Assert.Equal("Wrath II", updated.Value.Name);
        Assert.Equal(ClientOrigin.Manual, updated.Value.Origin);
        Assert.Equal(updated.Value.Id, new ServerStore(env.Config, new AtomicFileWriter()).Find(server.Id)!.ClientId);
        var stored = Assert.Single(new ClientStore(env.Config, new AtomicFileWriter()).List());
        Assert.Equal("Wrath II", stored.Name);
        Assert.Equal(ClientOrigin.Manual, stored.Origin);
        using var state = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(env.Config, "state.json")));
        Assert.True(state.RootElement.GetProperty("active_by_client").TryGetProperty(updated.Value.Id, out _));
        Assert.False(state.RootElement.GetProperty("active_by_client").TryGetProperty(client.Id, out _));
        Assert.False(state.RootElement.GetProperty("undo_by_client").TryGetProperty(client.Id, out _));
    }

    [Fact]
    public void ClientUpdateAndRemoval_AreBlockedWhileClientRuns()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client).Value!;
        env.GameProbe.RunningPaths.Add(Path.GetFullPath(env.Client));

        var update = env.Service.UpdateClient(new UpdateClientRequest(client.Id, "Busy"));
        var remove = env.Service.RemoveClient(client.Id);

        Assert.Equal(ServiceErrorCode.GameRunning, update.Error!.Code);
        Assert.Equal(ServiceErrorCode.GameRunning, remove.Error!.Code);
        Assert.Single(env.Service.ListClients().Value!);
    }

    [Fact]
    public void RemoveClient_ClearsServerPinAndPerClientState()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client).Value!;
        var server = env.Service.AddServer(new NewServer("Alpha", "alpha.example.org", ClientId: client.Id)).Value!;
        File.WriteAllText(Path.Combine(env.Config, "state.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1,
            active_id = server.Id,
            undo = (object?)null,
            active_by_client = new Dictionary<string, string> { [client.Id] = server.Id },
            undo_by_client = new Dictionary<string, object> { [client.Id] = new { server_id = (string?)null } },
        }));

        Assert.True(env.Service.RemoveClient(client.Id).Succeeded);

        Assert.Null(new ServerStore(env.Config, new AtomicFileWriter()).Find(server.Id)!.ClientId);
        using var state = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(env.Config, "state.json")));
        Assert.False(state.RootElement.GetProperty("active_by_client").TryGetProperty(client.Id, out _));
        Assert.False(state.RootElement.GetProperty("undo_by_client").TryGetProperty(client.Id, out _));
    }

    [Fact]
    public void ServerUpdate_ValidatesUniquenessAndRefreshesActiveProjection()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client).Value!;
        var active = env.Service.AddServer(new NewServer("Alpha", "alpha.example.org", ClientId: client.Id)).Value!;
        var other = env.Service.AddServer(new NewServer("Beta", "beta.example.org")).Value!;
        Assert.True(env.Service.SwitchServer(new SwitchRequest(client.Id, active.Id)).Succeeded);

        var duplicate = env.Service.UpdateServer(new UpdateServerRequest(active.Id, "BETA", active.Host));
        Assert.Equal(ServiceErrorCode.ServerNameTaken, duplicate.Error!.Code);

        var updated = env.Service.UpdateServer(new UpdateServerRequest(active.Id, "Alpha 2", "new.example.org", Realm: "Realm"));

        Assert.True(updated.Succeeded);
        var server = new ServerStore(env.Config, new AtomicFileWriter()).Find(active.Id)!;
        Assert.Equal("new.example.org", server.Host);
        Assert.Equal(active.Id, new ActiveServerManager(new ServerStore(env.Config, new AtomicFileWriter()), env.Config, env.Projection, new AtomicFileWriter()).GetActive()!.Id);
        Assert.Equal(ActiveProjection.Serialize(server), File.ReadAllBytes(ActiveProjection.FilePath(env.Projection)));
        Assert.Contains(env.Service.GetStatus().Value!.ClientsNotMatchingActive, c => c.Id == client.Id);
        Assert.True(env.Service.RemoveServer(other.Id).Succeeded);
    }

    [Fact]
    public void EditingOrRemovingActiveServer_IsBlockedWhenItsClientRuns()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client).Value!;
        var server = env.Service.AddServer(new NewServer("Alpha", "alpha.example.org", ClientId: client.Id)).Value!;
        Assert.True(env.Service.SwitchServer(new SwitchRequest(client.Id, server.Id)).Succeeded);
        env.GameProbe.RunningPaths.Add(Path.GetFullPath(env.Client));

        var update = env.Service.UpdateServer(new UpdateServerRequest(server.Id, server.Name, "new.example.org"));
        var realmUpdate = env.Service.UpdateServer(new UpdateServerRequest(server.Id, server.Name, server.Host, Realm: "Changed"));
        var remove = env.Service.RemoveServer(server.Id);

        Assert.Equal(ServiceErrorCode.GameRunning, update.Error!.Code);
        Assert.Equal(ServiceErrorCode.GameRunning, realmUpdate.Error!.Code);
        Assert.Equal(ServiceErrorCode.GameRunning, remove.Error!.Code);
        Assert.NotNull(new ServerStore(env.Config, new AtomicFileWriter()).Find(server.Id));
    }

    [Fact]
    public void ServerMetadataEdit_IsAllowedWhileGameRuns()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client).Value!;
        var server = env.Service.AddServer(new NewServer("Alpha", "alpha.example.org", ClientId: client.Id)).Value!;
        Assert.True(env.Service.SwitchServer(new SwitchRequest(client.Id, server.Id)).Succeeded);
        env.GameProbe.RunningPaths.Add(Path.GetFullPath(env.Client));

        var renamed = env.Service.UpdateServer(new UpdateServerRequest(server.Id, "Renamed", server.Host));

        Assert.True(renamed.Succeeded);
        Assert.Equal("Renamed", new ActiveServerManager(new ServerStore(env.Config, new AtomicFileWriter()), env.Config, env.Projection, new AtomicFileWriter()).GetActive()!.Name);
    }
}
