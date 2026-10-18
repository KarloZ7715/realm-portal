using System.Text.Json;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class FindingERecoveryHashGateTests
{
    [Fact]
    public void StartupRecovery_WithTamperedClientFiles_DoesNotCompleteStateUndo()
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
        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist orig.example.org\r\n"u8.ToArray());

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);

        var journalPath = Path.Combine(config, "last-switch.json");
        var backupId = JsonDocument.Parse(File.ReadAllText(journalPath)).RootElement.GetProperty("backup_id").GetString();
        Assert.NotNull(backupId);

        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist tampered.example.org\r\n"u8.ToArray());
        File.WriteAllText(journalPath, JsonSerializer.Serialize(new
        {
            version = 1,
            client_id = clientId,
            backup_id = backupId,
            files_undone = true,
        }) + "\n");

        var recovered = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);
        Assert.Equal(b.Id, recovered.GetStatus().Value!.Active!.Id);
        Assert.True(File.Exists(journalPath));
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
