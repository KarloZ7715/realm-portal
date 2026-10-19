using System.Text;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class PortalServiceTests
{
    private const string BlobA = "set realmlist logon-a.example.org\r\n";
    private const string BlobB = "set realmlist logon-b.example.org\r\n";

    private static readonly ClientVersion V3 = new(new Version(3, 3, 5, 12340));

    private sealed class FakeLaunch : IGameLaunchService
    {
        public int Launches { get; private set; }

        public void Launch(GameLaunchSpec spec) => Launches++;
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public HashSet<string> RunningPaths { get; } = new(StringComparer.Ordinal);
        public bool IsGameRunning(string clientRoot) => RunningPaths.Contains(Path.GetFullPath(clientRoot));
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Client = _temp.Combine("client");
            ClientB = _temp.Combine("client-b");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Projection);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Directory.CreateDirectory(Path.Combine(ClientB, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(ClientB, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Directory.CreateDirectory(Backups);
            Launch = new FakeLaunch();
            Probe = new NeverRunning();
            Service = new PortalService(
                Config,
                Projection,
                new PeClientVersionReader(),
                Launch,
                Probe,
                _ => Backups);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Client { get; }
        public string ClientB { get; }
        public string Backups { get; }
        public FakeLaunch Launch { get; }
        public NeverRunning Probe { get; }
        public PortalService Service { get; }

        public string ClientId => Service.AddClient(Client).Value!.Id;

        public ServerEntry AddServer(string name, string host, string blob = BlobA) =>
            Service.AddServer(new NewServer(name, host, ExtraRealmlistLines: blob == BlobA ? null : [blob.Replace("set realmlist ", "set patchlist ").Trim()])).Value!;

        public ServerEntry AddServerWithRealmlist(string name, string host, string realmlist)
        {
            var s = Server.Create(name, host, extraRealmlistLines: realmlist == BlobA ? null : []);
            // Build via service using host only then patch store - simpler: use host matching blob
            return Service.AddServer(new NewServer(name, host)).Value!;
        }

        public void PutRealmlist(string content) =>
            File.WriteAllBytes(Path.Combine(Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1.GetBytes(content));

        public string ReadRealmlist() =>
            File.ReadAllText(Path.Combine(Client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1);

        public void Dispose() => _temp.Dispose();
    }

    private static ServerEntry Add(string name, string host, PortalService service) =>
        service.AddServer(new NewServer(name, host)).Value!;

    [Fact]
    public void Switch_ThenUndo_RestoresFilesAndActiveServer()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var a = Add("A", "a.example.org", env.Service);
        var b = Add("B", "b.example.org", env.Service);

        env.PutRealmlist("set realmlist old.example.org\r\n");
        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        var sw = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id));
        Assert.True(sw.Succeeded);
        Assert.Contains("b.example.org", env.ReadRealmlist());

        var undo = env.Service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(a.Id, undo.Value!.Active?.Id);
        Assert.Contains("a.example.org", env.ReadRealmlist());
    }

    [Fact]
    public void ReswitchSameServer_EmptyPlan_KeepsPendingUndo_AndPreviousEqualsActive()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var a = Add("A", "a.example.org", env.Service);
        var b = Add("B", "b.example.org", env.Service);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);
        Assert.True(env.Service.GetStatus().Value!.CanUndo);

        var again = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id));
        Assert.True(again.Succeeded);
        Assert.Equal(b.Id, again.Value!.Active.Id);
        Assert.Equal(b.Id, again.Value.Previous?.Id);
        Assert.Null(again.Value.BackupId);
        Assert.True(env.Service.GetStatus().Value!.CanUndo);

        var undo = env.Service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(a.Id, undo.Value!.Active?.Id);
    }

    [Fact]
    public void ReswitchSameServer_DriftedFiles_Repairs_AndKeepsOriginalJournalBackup()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var b = Add("B", "b.example.org", env.Service);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);
        var journalAfterFirstSwitch = File.ReadAllText(Path.Combine(env.Config, "last-switch.json"));

        env.PutRealmlist("set realmlist tamper.example.org\r\n");

        var steps = new List<ServiceProgress>();
        var repair = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id), new Progress<ServiceProgress>(steps.Add));
        Assert.True(repair.Succeeded);
        Assert.Equal(journalAfterFirstSwitch, File.ReadAllText(Path.Combine(env.Config, "last-switch.json")));
        Assert.NotNull(repair.Value!.BackupId);
        Assert.Contains(b.Host, env.ReadRealmlist());
        Assert.Contains(steps, s => s.Step == ServiceStep.WriteFiles && s.State == StepState.Completed);
        Assert.Equal(b.Id, repair.Value.Previous?.Id);
    }

    [Fact]
    public void DiscardUndo_ClearsCanUndo_WithoutTouchingClientFiles()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var a = Add("A", "a.example.org", env.Service);
        var b = Add("B", "b.example.org", env.Service);
        env.PutRealmlist("set realmlist keep.example.org\r\n");

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);
        var before = env.ReadRealmlist();

        var discard = env.Service.DiscardUndo();
        Assert.True(discard.Succeeded);
        Assert.False(discard.Value!.CanUndo);
        Assert.Equal(before, env.ReadRealmlist());
        Assert.Equal(b.Id, discard.Value.Active?.Id);
        Assert.Equal(ServiceErrorCode.NothingToUndo, env.Service.Undo().Error!.Code);
    }

    [Fact]
    public void IncompatibleClient_WithoutForce_Fails()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var classic = env.Service.AddServer(new NewServer("Classic", "c.example.org", RequiredClientBuild: 5875)).Value!;

        var result = env.Service.SwitchServer(new SwitchRequest(clientId, classic.Id));
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.IncompatibleClient, result.Error!.Code);
    }

    [Fact]
    public void ClientIds_AreStable()
    {
        using var env = new Env();
        var id1 = env.Service.AddClient(env.Client).Value!.Id;
        var id2 = env.Service.AddClient(env.Client).Value!.Id;
        Assert.Equal(id1, id2);
        Assert.StartsWith("c", id1);
        Assert.Equal(17, id1.Length);
    }

    [Fact]
    public void SwitchAndPlay_RunningClientIsBlockedWhileOtherClientPlaysWithPerClientState()
    {
        using var env = new Env();
        var clientA = env.Service.AddClient(env.Client).Value!;
        var clientB = env.Service.AddClient(env.ClientB).Value!;
        var server1 = env.AddServer("Server 1", "one.example.org");
        var server2 = env.AddServer("Server 2", "two.example.org");

        Assert.True(env.Service.Play(new SwitchRequest(clientA.Id, server1.Id)).Succeeded);
        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientA.Id, server2.Id)).Succeeded);
        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientA.Id, server1.Id)).Succeeded);
        env.Probe.RunningPaths.Add(Path.GetFullPath(clientA.Path));
        var aFiles = File.ReadAllBytes(Path.Combine(clientA.Path, "Data", "enUS", "realmlist.wtf"));

        var blockedSwitch = env.Service.SwitchServer(new SwitchRequest(clientA.Id, server2.Id));
        var blockedPlay = env.Service.Play(new SwitchRequest(clientA.Id, server2.Id));
        Assert.Equal(ServiceErrorCode.GameRunning, blockedSwitch.Error!.Code);
        Assert.Equal(Consistency.Unchanged, blockedSwitch.Error.Consistency);
        Assert.Equal(ServiceErrorCode.GameRunning, blockedPlay.Error!.Code);
        Assert.Equal(ServiceErrorCode.GameRunning, env.Service.Undo(clientA.Id).Error!.Code);
        Assert.Equal(aFiles, File.ReadAllBytes(Path.Combine(clientA.Path, "Data", "enUS", "realmlist.wtf")));

        Assert.True(env.Service.Play(new SwitchRequest(clientB.Id, server2.Id)).Succeeded);
        env.Probe.RunningPaths.Add(Path.GetFullPath(clientB.Path));
        var blockedSecondPlay = env.Service.Play(new SwitchRequest(clientB.Id, server1.Id));
        Assert.Equal(ServiceErrorCode.GameRunning, blockedSecondPlay.Error!.Code);
        env.Probe.RunningPaths.Remove(Path.GetFullPath(clientB.Path));
        Assert.True(env.Service.Undo(clientB.Id).Succeeded);
        Assert.Equal(server1.Id, env.Service.ListServers(clientA.Id).Value!.Single(s => s.IsActive).Id);
        Assert.Contains(env.Service.ListServers(clientA.Id).Value!, s => s.Id == server1.Id && s.IsActive);
        Assert.Equal(server1.Id, env.Service.GetStatus().Value!.ActiveByClient![clientA.Id].Id);
        Assert.DoesNotContain(clientB.Id, env.Service.GetStatus().Value!.ActiveByClient!.Keys);
        Assert.True(env.Service.RemoveClient(clientB.Id).Succeeded);
        env.Probe.RunningPaths.Remove(Path.GetFullPath(clientA.Path));
        var undoA = env.Service.Undo(clientA.Id);
        Assert.True(undoA.Succeeded, undoA.Error?.Diagnostic);
        Assert.Equal(server2.Id, undoA.Value!.Active?.Id);
        Assert.Equal(2, env.Launch.Launches);
    }

    [Fact]
    public void Undo_PendingStateWithoutJournal_ReportsNothingToUndo()
    {
        using var env = new Env();
        var client = env.Service.AddClient(env.Client).Value!;
        var server = env.AddServer("Server 1", "one.example.org");
        Assert.True(env.Service.Play(new SwitchRequest(client.Id, server.Id)).Succeeded);
        File.Delete(Path.Combine(env.Config, LastSwitchJournal.FileName));

        var undo = env.Service.Undo(client.Id);

        Assert.Equal(ServiceErrorCode.NothingToUndo, undo.Error!.Code);
    }

    /// <summary>Holds the config lock file the way another Realm Portal process would.</summary>
    private static FileStream HoldConfigLock(string config)
    {
        Directory.CreateDirectory(config);
        return new FileStream(
            Path.Combine(config, ConfigDirectoryLock.LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    [Fact]
    public void ActivateWithoutClientFiles_ReadsTheActiveStateUnderTheConfigLock()
    {
        using var env = new Env();
        var a = env.AddServer("Alpha", "a.example.org");
        Assert.True(env.Service.ActivateWithoutClientFiles(a.Id).Succeeded);
        var statePath = Path.Combine(env.Config, "state.json");
        var journalPath = Path.Combine(env.Config, "last-switch.json");
        Assert.True(File.Exists(statePath));

        File.Delete(journalPath);
        using var foreign = HoldConfigLock(env.Config);

        var call = Task.Run(() => env.Service.ActivateWithoutClientFiles(a.Id));

        Assert.False(call.Wait(TimeSpan.FromMilliseconds(400)), "the workflow must take the config lock before it reads the active state");

        // Another process clears the active state while this call is in flight, then lets go of the lock.
        File.Delete(statePath);
        foreign.Dispose();
        Assert.True(call.Wait(TimeSpan.FromSeconds(15)));
        Assert.True(call.Result.Succeeded, call.Result.Error?.Diagnostic ?? call.Result.Error?.Code.ToString());

        // The snapshot was taken under the lock, so it saw the cleared state and the journal was written.
        Assert.True(File.Exists(journalPath), "the active-state snapshot must be read under the config lock");
        Assert.True(File.Exists(statePath));
    }

    [Fact]
    public void SwitchServer_WhileTheConfigLockIsHeldElsewhere_WaitsBeforeChangingFiles()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var a = env.AddServer("Alpha", "a.example.org");
        env.PutRealmlist("set realmlist keep.example.org\r\n");
        using var foreign = HoldConfigLock(env.Config);

        var call = Task.Run(() => env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)));

        Assert.False(call.Wait(TimeSpan.FromMilliseconds(400)), "the workflow must take the config lock before it plans, writes files or saves the journal");
        Assert.Contains("keep.example.org", env.ReadRealmlist());
        Assert.False(File.Exists(Path.Combine(env.Config, "state.json")));

        foreign.Dispose();
        Assert.True(call.Wait(TimeSpan.FromSeconds(15)));
        Assert.True(call.Result.Succeeded, call.Result.Error?.Diagnostic ?? call.Result.Error?.Code.ToString());
        Assert.Contains("a.example.org", env.ReadRealmlist());
        Assert.True(File.Exists(Path.Combine(env.Config, "last-switch.json")));
    }

    [Fact]
    public void Undo_WhileTheConfigLockIsHeldElsewhere_WaitsBeforeCheckingState()
    {
        using var env = new Env();
        using var foreign = HoldConfigLock(env.Config);

        var call = Task.Run(() => env.Service.Undo());

        Assert.False(call.Wait(TimeSpan.FromMilliseconds(400)), "Undo must take the config lock before it reads or writes any config file");

        foreign.Dispose();
        Assert.True(call.Wait(TimeSpan.FromSeconds(15)));
        Assert.Equal(ServiceErrorCode.NothingToUndo, call.Result.Error!.Code);
    }
}
