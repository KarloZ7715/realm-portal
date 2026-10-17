using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

public class Bug10UndoWordingTests
{
    [Fact]
    public void Undo_AfterClientSwitch_PrintsRestoredFolderLine()
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
        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), Encoding.Latin1.GetBytes("set realmlist old.example.org\r\n"));

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, b.Id)).Succeeded);

        var undo = service.Undo();
        Assert.True(undo.Succeeded, undo.Error is null ? "undo failed" : $"{undo.Error.Code}: {undo.Error.Diagnostic}");
        Assert.Contains($"Client files: restored '{client}' to its state before the switch.", undo.Value!.ClientFilesNotice);
        Assert.DoesNotContain("Client files: updated", undo.Value!.ClientFilesNotice ?? "");
    }

    [Fact]
    public void Undo_StateOnly_WhenAllClientsMatchActive_PrintsNoClientFilesLines()
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
        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist a.example.org\r\n"u8.ToArray());

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        Assert.True(service.SwitchServer(new SwitchRequest(clientId, a.Id)).Succeeded);
        service.ActivateWithoutClientFiles(b.Id);

        var stdout = new StringWriter();
        var app = new CliApp(stdout, new StringWriter(), new PeClientVersionReader(), new FakeLauncher(), _ => null);
        Assert.Equal(0, app.Run("undo", ["--config-dir", config, "--projection-dir", projection]));
        Assert.DoesNotContain("Client files:", stdout.ToString());
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }
}
