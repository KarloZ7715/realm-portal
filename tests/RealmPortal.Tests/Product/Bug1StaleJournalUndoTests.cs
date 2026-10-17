using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

/// <summary>QA stale journal: state-only switches must not replay an old client-files backup on undo.</summary>
public class Bug1StaleJournalUndoTests
{
    [Fact]
    public void StateOnlySwitchesAfterClientSwitch_UndoRestoresServerAndMatchingRealmlist()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("StaleA", "stale-a.example.org")).Value!;
        var b = service.AddServer(new NewServer("StaleB", "stale-b.example.org")).Value!;

        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist orig.example.org\r\n"u8.ToArray());
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);

        Assert.True(service.ActivateWithoutClientFiles(b.Id).Succeeded);
        Assert.True(service.ActivateWithoutClientFiles(a.Id).Succeeded);

        var undo = service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(b.Id, undo.Value!.Active?.Id);
        var rl = File.ReadAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf"));
        Assert.Contains("stale-b.example.org", rl);
        Assert.DoesNotContain("orig.example.org", rl);
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
