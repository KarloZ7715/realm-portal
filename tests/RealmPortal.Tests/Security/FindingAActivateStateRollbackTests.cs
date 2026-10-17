using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class FindingAActivateStateRollbackTests
{
    [Fact]
    public void FailedActivoWrite_RolledBack_UndoDoesNotActivateTargetOrRewriteClient()
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
        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist keep-a.example.org\r\n"u8.ToArray());

        var writer = new FailSecondActivoWrite(projection);
        var service = new PortalService(
            config,
            projection,
            new PeClientVersionReader(),
            new NoLaunch(),
            new NeverRunning(),
            _ => backups,
            writer);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);

        var failed = service.SwitchServer(new SwitchRequest(clientId, b.Id));
        Assert.False(failed.Succeeded);
        Assert.Equal(Consistency.RolledBack, failed.Error!.Consistency);
        Assert.Equal(a.Id, service.GetStatus().Value!.Active!.Id);

        var undo = service.Undo();
        if (undo.Succeeded)
            Assert.NotEqual(b.Id, undo.Value!.Active?.Id);
        Assert.Equal(a.Id, service.GetStatus().Value!.Active?.Id ?? a.Id);
        Assert.Contains("keep-a.example.org", File.ReadAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf")));
        Assert.DoesNotContain("b.example.org", File.ReadAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf")));
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    private sealed class FailSecondActivoWrite(string projectionDir) : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();
        private int _activoWrites;

        public void WriteAllBytes(string path, byte[] content)
        {
            if (path == ActiveProjection.FilePath(projectionDir))
            {
                _activoWrites++;
                if (_activoWrites == 2)
                    throw new IOException("Injected activo.json write failure.");
            }

            _inner.WriteAllBytes(path, content);
        }
    }
}
