using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

/// <summary>QA-07a6 / QA-13d: undo must follow state.json when no client files were touched.</summary>
public class Bug2StateOnlyUndoTests
{
    [Fact]
    public void FirstSwitchWithoutClientFiles_UndoRemovesActivoJson()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), getVariable: _ => dir.Combine("backups"));
        service.AddServer(new NewServer("Wotlk", "wotlk.example.org"));
        var wotlk = service.ListServers().Value!.Single();

        Assert.True(service.ActivateWithoutClientFiles(wotlk.Id).Succeeded);
        var activo = ActiveProjection.FilePath(projection);
        Assert.True(File.Exists(activo));

        var undo = service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Null(undo.Value!.Active);
        Assert.False(File.Exists(activo));
    }

    [Fact]
    public void ReswitchSameServerWithoutClient_UndoDeactivates_Qa13d()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), getVariable: _ => dir.Combine("backups"));
        var alpha = service.AddServer(new NewServer("Alpha", "alpha.example.org")).Value!;
        var beta = service.AddServer(new NewServer("Beta", "beta.example.org")).Value!;

        Assert.True(service.ActivateWithoutClientFiles(alpha.Id).Succeeded);
        Assert.True(service.RemoveServer(alpha.Id).Succeeded);
        Assert.True(service.ActivateWithoutClientFiles(beta.Id).Succeeded);
        Assert.True(service.ActivateWithoutClientFiles(beta.Id).Succeeded);

        var undo = service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Null(undo.Value!.Active);
    }

    [Fact]
    public void SwitchWithoutClientFiles_ThenUndo_RestoresPreviousServer()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), getVariable: _ => dir.Combine("backups"));
        var wotlk = service.AddServer(new NewServer("Wotlk", "wotlk.example.org")).Value!;
        var second = service.AddServer(new NewServer("Second", "second.example.org")).Value!;

        Assert.True(service.ActivateWithoutClientFiles(wotlk.Id).Succeeded);
        Assert.True(service.ActivateWithoutClientFiles(second.Id).Succeeded);
        var undo = service.Undo();
        Assert.True(undo.Succeeded);
        Assert.Equal(wotlk.Id, undo.Value!.Active?.Id);
    }

    [Fact]
    public void StateOnlyUndo_WhenActivoWriteFails_IsUnchangedAndLeavesClientFilesAlone()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        var realmlist = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        var realmlistBytes = "set realmlist keep.example.org\r\n"u8.ToArray();
        File.WriteAllBytes(realmlist, realmlistBytes);

        var writer = new FailNextActivoWrite(projection);
        var service = new PortalService(
            config,
            projection,
            new PeClientVersionReader(),
            new NoLaunch(),
            getVariable: _ => dir.Combine("backups"),
            writer: writer);
        service.AddClient(client);
        var first = service.AddServer(new NewServer("First", "first.example.org")).Value!;
        var second = service.AddServer(new NewServer("Second", "second.example.org")).Value!;
        Assert.True(service.ActivateWithoutClientFiles(first.Id).Succeeded);
        Assert.True(service.ActivateWithoutClientFiles(second.Id).Succeeded);

        writer.Arm();
        var failed = service.Undo();

        Assert.False(failed.Succeeded);
        Assert.NotNull(failed.Error);
        Assert.Equal(Consistency.Unchanged, failed.Error.Consistency);
        Assert.Contains("Injected activo.json write failure.", failed.Error.Diagnostic);
        Assert.Equal(second.Id, service.GetStatus().Value!.Active!.Id);
        Assert.True(service.GetStatus().Value!.CanUndo);
        Assert.Contains("second.example.org", File.ReadAllText(ActiveProjection.FilePath(projection)));
        Assert.Equal(realmlistBytes, File.ReadAllBytes(realmlist));

        var retry = service.Undo();
        Assert.True(retry.Succeeded);
        Assert.Equal(first.Id, service.GetStatus().Value!.Active!.Id);
        Assert.Equal(realmlistBytes, File.ReadAllBytes(realmlist));
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    /// <summary>Fails the next <c>activo.json</c> write after <see cref="Arm"/>.</summary>
    private sealed class FailNextActivoWrite(string projectionDir) : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();
        private readonly string _activoPath = ActiveProjection.FilePath(projectionDir);
        private int _activoWrites;
        private int _failAt = -1;

        public void Arm() => _failAt = _activoWrites + 1;

        public void WriteAllBytes(string path, byte[] content)
        {
            if (path == _activoPath)
            {
                _activoWrites++;
                if (_activoWrites == _failAt)
                    throw new IOException("Injected activo.json write failure.");
            }

            _inner.WriteAllBytes(path, content);
        }
    }
}
