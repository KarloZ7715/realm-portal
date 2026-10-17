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
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Client = _temp.Combine("client");
            Backups = _temp.Combine("backups");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Projection);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Directory.CreateDirectory(Backups);
            Launch = new FakeLaunch();
            Service = new PortalService(
                Config,
                Projection,
                new PeClientVersionReader(),
                Launch,
                new NeverRunning(),
                _ => Backups);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Client { get; }
        public string Backups { get; }
        public FakeLaunch Launch { get; }
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
    public void ReswitchSameServer_DriftedFiles_Repairs_AndUpdatesJournalBackup()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var b = Add("B", "b.example.org", env.Service);

        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);
        var firstBackup = env.Service.GetStatus().Value!.BackupFolder;
        Assert.NotNull(Directory.GetDirectories(Path.Combine(firstBackup, clientId)));

        env.PutRealmlist("set realmlist tamper.example.org\r\n");

        var steps = new List<ServiceProgress>();
        var repair = env.Service.SwitchServer(new SwitchRequest(clientId, b.Id), new Progress<ServiceProgress>(steps.Add));
        Assert.True(repair.Succeeded);
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
    public void ActivateWithoutClientFiles_ThenUndo_RestoresPreviousActiveServer()
    {
        using var env = new Env();
        var wotlk = Add("Wotlk", "wotlk.example.org", env.Service);
        var second = Add("Second", "second.example.org", env.Service);

        Assert.True(env.Service.ActivateWithoutClientFiles(wotlk.Id).Succeeded);
        Assert.True(env.Service.ActivateWithoutClientFiles(second.Id).Succeeded);
        var undo = env.Service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal("Wotlk", undo.Value!.Active?.Name);
    }

    [Fact]
    public void StaleJournal_StateOnlySwitchesAfterClientSwitch_UndoDoesNotReplayOldBackup()
    {
        using var env = new Env();
        var clientId = env.ClientId;
        var a = env.Service.AddServer(new NewServer("StaleA", "stale-a.example.org")).Value!;
        var b = env.Service.AddServer(new NewServer("StaleB", "stale-b.example.org")).Value!;

        env.PutRealmlist("set realmlist orig.example.org\r\n");
        Assert.True(env.Service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.Contains("stale-a.example.org", env.ReadRealmlist());

        Assert.True(env.Service.ActivateWithoutClientFiles(b.Id).Succeeded);
        Assert.True(env.Service.ActivateWithoutClientFiles(a.Id).Succeeded);

        var undo = env.Service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(b.Id, undo.Value!.Active?.Id);
        Assert.Contains("stale-b.example.org", env.ReadRealmlist());
        Assert.DoesNotContain("orig.example.org", env.ReadRealmlist());
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
}
