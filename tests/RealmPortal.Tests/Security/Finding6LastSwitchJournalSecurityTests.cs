using System.Text.Json;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding6LastSwitchJournalSecurityTests
{
    [Fact]
    public void Load_RejectsInvalidClientOrBackupId()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        Directory.CreateDirectory(config);
        var path = Path.Combine(config, "last-switch.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = 1, client_id = "not-valid", backup_id = "bad" }) + "\n");

        var service = new PortalService(
            config,
            dir.Combine("projection"),
            new PeClientVersionReader(),
            new NoLaunch(),
            getVariable: _ => dir.Combine("backups"));

        var undo = service.Undo();
        Assert.False(undo.Succeeded);
        Assert.False(service.GetStatus().Value!.CanUndo);
    }

    [Fact]
    public void DiscardUndo_ClearsStateBeforeJournal()
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
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        service.SwitchServer(new SwitchRequest(clientId, a.Id));
        service.SwitchServer(new SwitchRequest(clientId, b.Id));

        Assert.True(service.DiscardUndo().Succeeded);
        Assert.False(File.Exists(Path.Combine(config, "last-switch.json")));
        Assert.False(service.GetStatus().Value!.CanUndo);
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class NoLaunch : RealmPortal.Core.Launch.IGameLaunchService
    {
        public void Launch(RealmPortal.Core.Launch.GameLaunchSpec spec) { }
    }
}
