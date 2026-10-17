using System.Text;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

/// <summary>QA #43: tampered backup manifests must not restore paths outside the managed allow-list.</summary>
public class Finding43ManifestAllowListTests
{
    [Theory]
    [InlineData("Interface/AddOns/keep.txt", "user addon data must survive")]
    [InlineData("Wow.exe", null)]
    public void PortalUndo_WithTamperedManifestPath_FailsAndLeavesTargetBytes(string manifestPath, string? addonContent)
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
        if (addonContent is not null)
            Directory.CreateDirectory(Path.Combine(client, "Interface", "AddOns"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist old.example.org\r\n"u8.ToArray());
        if (addonContent is not null)
            File.WriteAllText(Path.Combine(client, "Interface", "AddOns", "keep.txt"), addonContent);

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);

        var journal = Path.Combine(config, "last-switch.json");
        Assert.True(File.Exists(journal));
        var backupId = System.Text.Json.JsonDocument.Parse(File.ReadAllText(journal)).RootElement.GetProperty("backup_id").GetString()!;
        var manifestFile = Path.Combine(backups, clientId, backupId, "manifest.json");
        var tampered = File.ReadAllText(manifestFile).Replace("Data/enUS/realmlist.wtf", manifestPath);
        File.WriteAllText(manifestFile, tampered);

        byte[]? wowBefore = manifestPath == "Wow.exe" ? File.ReadAllBytes(Path.Combine(client, "Wow.exe")) : null;
        var keepBefore = addonContent is not null ? File.ReadAllText(Path.Combine(client, "Interface", "AddOns", "keep.txt")) : null;

        var undo = service.Undo();
        Assert.False(undo.Succeeded);
        Assert.Equal(ServiceErrorCode.PathEscapesClient, undo.Error!.Code);

        if (wowBefore is not null)
            Assert.Equal(wowBefore, File.ReadAllBytes(Path.Combine(client, "Wow.exe")));
        if (keepBefore is not null)
            Assert.Equal(keepBefore, File.ReadAllText(Path.Combine(client, "Interface", "AddOns", "keep.txt")));

        var status = service.GetStatus().Value!;
        Assert.Equal(b.Name, status.Active!.Name);
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
