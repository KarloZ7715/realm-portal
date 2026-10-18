using System.ComponentModel;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class PortalServicePlayLaunchFailedTests
{
    [Fact]
    public void Play_LaunchThrowsAfterSwitch_IsAppliedLaunchFailed_AndUndoRestoresPrevious()
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
        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist old.example.org\r\n"u8.ToArray());

        var service = new PortalService(
            config,
            projection,
            new PeClientVersionReader(),
            new ThrowingLaunch(),
            new NeverRunning(),
            _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);

        var played = service.Play(new SwitchRequest(clientId, b.Id));
        Assert.False(played.Succeeded);
        Assert.Equal(ServiceErrorCode.LaunchFailed, played.Error!.Code);
        Assert.Equal(Consistency.AppliedLaunchFailed, played.Error.Consistency);

        var status = service.GetStatus().Value!;
        Assert.Equal(b.Id, status.Active!.Id);
        Assert.True(status.CanUndo);
        var realmlist = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        Assert.Contains("b.example.org", File.ReadAllText(realmlist));

        var undo = service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(a.Id, undo.Value!.Active?.Id);
        Assert.Equal(a.Id, service.GetStatus().Value!.Active!.Id);
        Assert.Contains("a.example.org", File.ReadAllText(realmlist));
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    /// <summary>LaunchGame wraps this in <see cref="InvalidOperationException"/>.</summary>
    private sealed class ThrowingLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) =>
            throw new Win32Exception("Injected process start failure.");
    }
}
