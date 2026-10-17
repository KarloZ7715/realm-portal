using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Product;

public class Bug9UndoClientFilesUxTests
{
    [Fact]
    public void Undo_WithTwoClients_PrintsNotChangedLine_AndStatusFlagsDrift()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var c1 = dir.Combine("c1");
        var c2 = dir.Combine("c2");
        var backups = dir.Combine("backups");
        foreach (var path in new[] { config, projection, backups, Path.Combine(c1, "Data", "enUS"), Path.Combine(c2, "Data", "enUS") })
            Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(c1, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllBytes(Path.Combine(c2, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllBytes(Path.Combine(c1, "Data", "enUS", "realmlist.wtf"), "set realmlist a.example.org\r\n"u8.ToArray());
        File.WriteAllBytes(Path.Combine(c2, "Data", "enUS", "realmlist.wtf"), "set realmlist a.example.org\r\n"u8.ToArray());

        var service = new PortalService(config, projection, new PeClientVersionReader(), new NoLaunch(), new NeverRunning(), _ => backups);
        service.AddClient(c1);
        service.AddClient(c2);
        var a = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var b = service.AddServer(new NewServer("B", "b.example.org")).Value!;
        service.ActivateWithoutClientFiles(a.Id);
        service.ActivateWithoutClientFiles(b.Id);
        service.ActivateWithoutClientFiles(a.Id);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var app = new CliApp(stdout, stderr, new PeClientVersionReader(), new FakeLauncher(), _ => null);
        Assert.Equal(0, app.Run("undo", ["--config-dir", config, "--projection-dir", projection]));
        var outText = stdout.ToString();
        Assert.Contains("Client files: not changed.", outText);
        Assert.Contains("Client folder out of sync:", outText);
        Assert.DoesNotContain("run 'switch A --client-dir", outText);

        var statusOut = new StringWriter();
        var statusApp = new CliApp(statusOut, new StringWriter(), new PeClientVersionReader(), new FakeLauncher(), _ => null);
        Assert.Equal(0, statusApp.Run("status", ["--config-dir", config, "--projection-dir", projection]));
        Assert.Contains("Client folder out of sync:", statusOut.ToString());
    }

    [Fact]
    public void Undo_StateOnly_WhenClientAlreadyMatchesActive_PrintsNoClientFilesLine()
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
