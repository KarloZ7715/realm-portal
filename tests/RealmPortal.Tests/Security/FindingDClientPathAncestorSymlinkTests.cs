using System.Runtime.InteropServices;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class FindingDClientPathAncestorSymlinkTests
{
    [Fact]
    public void AddClient_RejectsSymlinkInParentPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        using var dir = new TempDirectory();
        var realRoot = dir.Combine("real");
        var linkRoot = dir.Combine("link");
        var client = Path.Combine(linkRoot, "client");
        Directory.CreateDirectory(Path.Combine(realRoot, "client", "Data", "enUS"));
        File.CreateSymbolicLink(linkRoot, realRoot);
        File.WriteAllBytes(Path.Combine(realRoot, "client", "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        var service = new PortalService(dir.Combine("config"), dir.Combine("projection"), new PeClientVersionReader(), new NoLaunch());
        var result = service.AddClient(client);
        Assert.False(result.Succeeded);
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) { }
    }
}
