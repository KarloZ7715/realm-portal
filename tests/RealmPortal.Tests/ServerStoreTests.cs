using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ServerStoreTests
{
    private static ServerStore NewStore(string dir) => new(dir, new AtomicFileWriter());

    [Fact]
    public void MissingFile_IsEmptyStore_AndNothingIsCreatedByReading()
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Combine("cfg"));

        Assert.Empty(store.List());
        Assert.False(Directory.Exists(dir.Combine("cfg")));
    }

    [Fact]
    public void Add_List_Remove_Work()
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Path);
        var a = store.Add(Server.Create("Alpha", "a.example.org"));
        var b = store.Add(Server.Create("Beta", "b.example.org", requiredClientBuild: 12340));

        Assert.Equal([a, b], store.List());
        Assert.True(store.Remove(a.Id));
        Assert.False(store.Remove(a.Id));
        Assert.Equal([b], store.List());
    }

    [Theory]
    [InlineData("SET REALMLIST h\r\n", InvalidRealmlistKind.WrongFirstLine)]
    [InlineData("set realmlist h\r\nset portal x\r\n", InvalidRealmlistKind.ForbiddenKey)]
    [InlineData("set realmlist h\u00f3\r\n", InvalidRealmlistKind.NotAscii)]
    public void Add_RejectsServerWithInvalidRealmlist_AndWritesNothing(string realmlist, InvalidRealmlistKind kind)
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Combine("cfg"));
        var bad = new Server(new string('a', 32), "Bad", "h", "", realmlist, null, null);

        var ex = Assert.Throws<InvalidRealmlistException>(() => store.Add(bad));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal("Bad", ex.ServerName);
        Assert.False(Directory.Exists(dir.Combine("cfg")));
    }

    [Fact]
    public void Add_RejectsDuplicateNameIgnoringCase()
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Path);
        store.Add(Server.Create("Alpha", "a.example.org"));

        Assert.Throws<StoreException>(() => store.Add(Server.Create("ALPHA", "other.example.org")));
        Assert.Single(store.List());
    }

    [Fact]
    public void Find_ByIdNameAndUniquePrefix()
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Path);
        var a = store.Add(Server.Create("Alpha", "a", id: "abc11100000000000000000000000000"));
        var b = store.Add(Server.Create("Beta", "b", id: "abc22200000000000000000000000000"));

        Assert.Equal(a, store.Find("abc11100000000000000000000000000"));
        Assert.Equal(b, store.Find("beta"));
        Assert.Equal(b, store.Find("abc2"));
        Assert.Null(store.Find("abc")); // ambiguous prefix
        Assert.Null(store.Find(""));
        Assert.Null(store.Find("nope"));
    }

    [Fact]
    public void FileFormat_IsDeterministic_Utf8NoBom_WithTrailingNewline()
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Path);
        store.Add(Server.Create("Ñandú", "h.example.org", "R&1", ["set patchlist p.example.org"], 12340, "carlos", id: "11111111111111111111111111111111"));

        var expected =
            "{\n" +
            "  \"version\": 1,\n" +
            "  \"servers\": [\n" +
            "    {\n" +
            "      \"id\": \"11111111111111111111111111111111\",\n" +
            "      \"name\": \"Ñandú\",\n" +
            "      \"host\": \"h.example.org\",\n" +
            "      \"realm\": \"R&1\",\n" +
            "      \"realmlist\": \"set realmlist h.example.org\\r\\nset patchlist p.example.org\\r\\n\",\n" +
            "      \"required_client_build\": 12340,\n" +
            "      \"account\": \"carlos\"\n" +
            "    }\n" +
            "  ]\n" +
            "}\n";
        var bytes = File.ReadAllBytes(store.FilePath);
        Assert.Equal(new System.Text.UTF8Encoding(false).GetBytes(expected), bytes);
        Assert.NotEqual(0xEF, bytes[0]); // no BOM
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"version\": 2, \"servers\": []}")]
    [InlineData("{\"version\": 1, \"servers\": [{\"id\": \"x\"}]}")]
    [InlineData("{\"version\": 1, \"servers\": [{\"id\":\"x\",\"name\":\"n\",\"host\":\"h\",\"realmlist\":\"r\"},{\"id\":\"x\",\"name\":\"m\",\"host\":\"h\",\"realmlist\":\"r\"}]}")]
    public void CorruptOrUnsupportedFile_ThrowsAndIsNotOverwritten(string content)
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Path);
        File.WriteAllText(store.FilePath, content);

        Assert.Throws<StoreException>(() => store.List());
        Assert.Throws<StoreException>(() => store.Add(Server.Create("n", "h")));
        Assert.Equal(content, File.ReadAllText(store.FilePath));
    }

    [Property(MaxTest = 60)]
    public Property SaveThenLoad_RoundTripsAnyServers() =>
        Prop.ForAll(ServerGen.DistinctServers().ToArbitrary(), servers =>
        {
            using var dir = new TempDirectory();
            var store = NewStore(dir.Path);
            foreach (var server in servers)
                store.Add(server);

            // A second store instance reads what the first one wrote.
            var reloaded = NewStore(dir.Path).List();
            return servers.SequenceEqual(reloaded);
        });

    [Property(MaxTest = 60)]
    public Property RemoveEach_LeavesEmptyStore() =>
        Prop.ForAll(ServerGen.DistinctServers().ToArbitrary(), servers =>
        {
            using var dir = new TempDirectory();
            var store = NewStore(dir.Path);
            foreach (var server in servers)
                store.Add(server);
            foreach (var server in servers)
                store.Remove(server.Id);
            return store.List().Count == 0;
        });

    [Fact]
    public void Add_PersistsEveryServer()
    {
        using var dir = new TempDirectory();
        var store = NewStore(dir.Combine("config"));

        Parallel.For(0, 8, i =>
            store.Add(Server.Create($"Q{i}", $"q{i}.example.org")));

        var hosts = store.List().Select(s => s.Host).OrderBy(h => h).ToList();
        Assert.Equal(
            Enumerable.Range(0, 8).Select(i => $"q{i}.example.org").OrderBy(h => h),
            hosts);
    }
}
