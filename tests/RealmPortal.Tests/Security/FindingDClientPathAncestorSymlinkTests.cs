using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

/// <summary>
/// Ancestors above a client folder may be symlinks (system mounts). The client folder itself must not be one,
/// the stored path stays the normalized path the user gave, and a managed folder inside the client that points
/// outside is still refused on switch.
/// </summary>
public class FindingDClientPathAncestorSymlinkTests
{
    [UnixFact]
    public void AddClient_AcceptsSymlinkedAncestor_AndStoresTheUnresolvedPath()
    {
        using var dir = new TempDirectory();
        var realRoot = dir.Combine("real");
        var linkRoot = dir.Combine("link");
        var client = Path.Combine(linkRoot, "client");
        var realClient = Path.Combine(realRoot, "client");
        Directory.CreateDirectory(Path.Combine(realClient, "Data", "enUS"));
        File.WriteAllText(Path.Combine(realClient, "Data", "enUS", "realmlist.wtf"), "set realmlist old.example.org\r\n");
        File.WriteAllBytes(Path.Combine(realClient, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        Directory.CreateSymbolicLink(linkRoot, realRoot);

        var service = CreateService(dir);
        var result = service.AddClient(client);

        Assert.True(result.Succeeded);
        var stored = result.Value!.Path;
        var normalized = Path.GetFullPath(client);
        Assert.Equal(normalized, stored);
        Assert.NotEqual(Path.GetFullPath(realClient), stored);
        Assert.Equal(ClientIds.FromFolderPath(normalized), result.Value.Id);
        var json = File.ReadAllText(Path.Combine(dir.Combine("config"), "clients.json"));
        Assert.Contains(normalized, json, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFullPath(realClient), json, StringComparison.Ordinal);

        var server = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var switched = service.SwitchServer(new SwitchRequest(result.Value.Id, server.Id));
        Assert.True(switched.Succeeded);
        Assert.Contains("a.example.org", File.ReadAllText(Path.Combine(realClient, "Data", "enUS", "realmlist.wtf")), StringComparison.Ordinal);
    }

    [UnixFact]
    public void AddClient_RejectsWhenTheClientFolderItselfIsASymlink()
    {
        using var dir = new TempDirectory();
        var realClient = dir.Combine("real-client");
        var linkClient = dir.Combine("client-link");
        Directory.CreateDirectory(Path.Combine(realClient, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(realClient, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        Directory.CreateSymbolicLink(linkClient, realClient);

        var service = CreateService(dir);
        var result = service.AddClient(linkClient);

        Assert.False(result.Succeeded);
        Assert.Contains("symbolic link", result.Error!.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(dir.Combine("config"), "clients.json")));
    }

    [UnixFact]
    public void Switch_RefusesDataLocaleSymlinkPointingOutside_UnderASymlinkedAncestor()
    {
        using var dir = new TempDirectory();
        var realRoot = dir.Combine("real");
        var linkRoot = dir.Combine("link");
        var outside = dir.Combine("outside");
        var client = Path.Combine(linkRoot, "client");
        var realClient = Path.Combine(realRoot, "client");
        Directory.CreateDirectory(Path.Combine(realClient, "Data"));
        Directory.CreateDirectory(Path.Combine(outside, "enUS"));
        var outsideRealmlist = Path.Combine(outside, "enUS", "realmlist.wtf");
        File.WriteAllText(outsideRealmlist, "set realmlist old.example.org\r\n");
        File.WriteAllBytes(Path.Combine(realClient, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        Directory.CreateSymbolicLink(Path.Combine(realClient, "Data", "enUS"), Path.Combine(outside, "enUS"));
        Directory.CreateSymbolicLink(linkRoot, realRoot);
        var outsideBefore = File.ReadAllText(outsideRealmlist);

        var service = CreateService(dir);
        var added = service.AddClient(client);
        Assert.True(added.Succeeded);
        Assert.Equal(Path.GetFullPath(client), added.Value!.Path);

        var server = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        var result = service.SwitchServer(new SwitchRequest(added.Value.Id, server.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.PathEscapesClient, result.Error!.Code);
        Assert.Equal(outsideBefore, File.ReadAllText(outsideRealmlist));
    }

    private static PortalService CreateService(TempDirectory dir)
    {
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(backups);
        return new PortalService(
            dir.Combine("config"),
            dir.Combine("projection"),
            new PeClientVersionReader(),
            new NoLaunch(),
            new NeverRunning(),
            _ => backups);
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
