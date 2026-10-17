using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ServerStoreConcurrencyTests
{
    [Fact]
    public void ParallelAdd_PersistsEveryServer()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var store = new ServerStore(config, new AtomicFileWriter());

        Parallel.For(0, 8, i =>
            store.Add(Server.Create($"Q{i}", $"q{i}.example.org")));

        var hosts = store.List().Select(s => s.Host).OrderBy(h => h).ToList();
        Assert.Equal(
            Enumerable.Range(0, 8).Select(i => $"q{i}.example.org").OrderBy(h => h),
            hosts);
    }

    [Fact]
    public void ParallelSwitch_KeepsProjectionInSync()
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var store = new ServerStore(config, new AtomicFileWriter());
        var manager = new ActiveServerManager(store, config, projection, new AtomicFileWriter());
        var servers = Enumerable.Range(1, 6)
            .Select(i => store.Add(Server.Create($"P{i}", $"p{i}.example.org")))
            .ToList();

        for (var round = 0; round < 8; round++)
        {
            Parallel.ForEach(servers, s => manager.Switch(s));
            var status = manager.Status();
            Assert.True(status.ProjectionInSync);
            Assert.NotNull(status.Active);
            Assert.Equal(status.Active!.Id, manager.GetActive()!.Id);
        }
    }
}
