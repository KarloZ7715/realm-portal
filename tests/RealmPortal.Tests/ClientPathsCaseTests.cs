using RealmPortal.Core.ClientFiles;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientPathsCaseTests
{
    [LinuxFact]
    public void ResolveManagedPath_RejectsAmbiguousCaseVariants()
    {
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        Directory.CreateDirectory(Path.Combine(client, "wtf"));

        var ex = Assert.Throws<ClientFilesException>(() =>
            ClientPaths.ResolveManagedPath(ClientPaths.CanonicalRoot(client), "WTF/Config.wtf"));
        Assert.Equal(ClientFilesErrorCode.PathEscapesClient, ex.Code);
    }

    [LinuxFact]
    public void ResolveManagedPath_UsesExistingDirectoryCasing_OnLinux()
    {
        using var temp = new TempDirectory();
        var client = temp.Combine("client");
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        File.WriteAllText(Path.Combine(client, "WTF", "Config.wtf"), "x");

        var root = ClientPaths.CanonicalRoot(client);
        var resolved = ClientPaths.ResolveManagedPath(root, "wtf/Config.wtf");

        Assert.Equal(Path.Combine(client, "WTF", "Config.wtf"), resolved);
        Assert.False(File.Exists(Path.Combine(client, "wtf", "Config.wtf")));
    }
}
