using RealmPortal.Core.Clients;
using RealmPortal.Core.IO;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests.Security;

public class Finding3ClientStoreSecurityTests
{
    [Fact]
    public void Load_RejectsClientIdThatDoesNotMatchCanonicalPath()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        Directory.CreateDirectory(config);
        var client = dir.Combine("client");
        Directory.CreateDirectory(client);

        var store = new ClientStore(config, new AtomicFileWriter());
        var legit = store.Add(client);
        var json = File.ReadAllText(store.FilePath).Replace(legit.Id, "c0000000000000000");

        File.WriteAllText(store.FilePath, json);

        Assert.Throws<RealmPortal.Core.Servers.StoreException>(() => store.List());
    }

    [Fact]
    public void Add_RejectsSymlinkClientRoot()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        Directory.CreateDirectory(config);
        var real = dir.Combine("real-client");
        Directory.CreateDirectory(real);
        var link = dir.Combine("client-link");
        File.CreateSymbolicLink(link, real);

        var store = new ClientStore(config, new AtomicFileWriter());
        Assert.Throws<RealmPortal.Core.Servers.StoreException>(() => store.Add(link));
    }
}
