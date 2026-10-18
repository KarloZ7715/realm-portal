using System.Text.Json;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class FindingBRepairJournalBackupTests
{
    [Fact]
    public void TamperThenRepairSameServer_UndoRestoresOriginalSwitchBytes_NotTampered()
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
        var journalAfterB = LoadBackupId(config);
        Assert.NotNull(journalAfterB);

        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist tamper.example.org\r\n"u8.ToArray());
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);
        Assert.Equal(journalAfterB, LoadBackupId(config));

        var undo = service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(a.Id, undo.Value!.Active?.Id);
        var rl = File.ReadAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf"));
        Assert.Contains("a.example.org", rl);
        Assert.DoesNotContain("tamper.example.org", rl);
    }

    private static string? LoadBackupId(string configDir)
    {
        var path = Path.Combine(configDir, "last-switch.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty("backup_id", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
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
