using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ClientIdsTests
{
    [Fact]
    public void FromFolderPath_IgnoresTrailingDirectorySeparator()
    {
        using var dir = new TempDirectory();
        var client = dir.Combine("client");
        Directory.CreateDirectory(client);

        var canonical = ClientPaths.CanonicalRoot(client);
        var withSeparator = canonical + Path.DirectorySeparatorChar;

        var id = ClientIds.FromFolderPath(canonical);
        Assert.Equal(id, ClientIds.FromFolderPath(client));
        Assert.Equal(id, ClientIds.FromFolderPath(withSeparator));
        Assert.Equal(id, ClientIds.FromFolderPath(withSeparator + Path.DirectorySeparatorChar));
    }
}
