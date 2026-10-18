using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class MissingClientFolderTests
{
    [Fact]
    public void MissingClientFolder_DoesNotFailLoad_AndOtherClientsStayUsable()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var backups = dir.Combine("backups");
        var good = dir.Combine("good");
        var missing = dir.Combine("missing");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(backups);
        WriteClient(good);
        WriteClient(missing);

        var service = Create(config, projection, backups);
        var goodId = service.AddClient(good).Value!.Id;
        var missingId = service.AddClient(missing).Value!.Id;
        var serverA = service.AddServer(new NewServer("Alpha", "a.example.org")).Value!;
        var serverB = service.AddServer(new NewServer("Beta", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(goodId, serverA.Id)).Succeeded);

        Directory.Move(missing, dir.Combine("missing-renamed"));

        var reloaded = Create(config, projection, backups);

        var listed = reloaded.ListClients();
        Assert.True(listed.Succeeded);
        Assert.Equal(2, listed.Value!.Count);
        var missingEntry = Assert.Single(listed.Value, c => c.Id == missingId);
        Assert.Equal(ServiceErrorCode.PathNotFound, missingEntry.Error!.Code);
        Assert.Null(missingEntry.Version);
        var goodEntry = Assert.Single(listed.Value, c => c.Id == goodId);
        Assert.Null(goodEntry.Error);
        Assert.NotNull(goodEntry.Version);

        var status = reloaded.GetStatus();
        Assert.True(status.Succeeded);
        Assert.Equal(serverA.Id, status.Value!.Active!.Id);

        var switched = reloaded.SwitchServer(new SwitchRequest(goodId, serverB.Id));
        Assert.True(switched.Succeeded);
        Assert.Equal(serverB.Id, reloaded.GetStatus().Value!.Active!.Id);

        var failed = reloaded.SwitchServer(new SwitchRequest(missingId, serverA.Id));
        Assert.False(failed.Succeeded);
        Assert.Equal(ServiceErrorCode.PathNotFound, failed.Error!.Code);
        Assert.Equal(missingEntry.Path, failed.Error.Path);
    }

    private static void WriteClient(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(root, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
    }

    private static PortalService Create(string config, string projection, string backups) =>
        new(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
