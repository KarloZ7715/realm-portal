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

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
