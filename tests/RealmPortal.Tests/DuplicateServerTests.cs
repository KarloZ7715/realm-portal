using System.Text.Json;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>Issue #191: <see cref="IPortalService.DuplicateServer"/> and <c>realm-portal duplicate</c> add an independent copy.</summary>
public sealed class DuplicateServerTests
{
    private const string PatchLine = "set patchlist patch.example.test";

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not used by these tests.");
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) =>
            throw new InvalidOperationException("Not used by these tests.");
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();

        public Box() => Service = new PortalService(Config, Projection, new PeClientVersionReader(), new NoLaunch(),
            new NeverRunningGameProbe(), TestBackupDir.Variables(_dir));

        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string ServersPath => Path.Combine(Config, ServerStore.FileName);
        public PortalService Service { get; }

        public string NewClient(string name)
        {
            var client = _dir.Combine(name);
            Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            return client;
        }

        public ServerEntry AddFull(string name = "Example")
        {
            var client = Service.AddClient(NewClient("client-" + Guid.NewGuid().ToString("N")[..8]), "Wrath").Value!;
            var added = Service.AddServer(new NewServer(name, "logon.example.test:3725", "PvP Realm", null, 12340, "player1",
                client.Id, ExtraRealmlistLines: [PatchLine], ClearCacheOnSwitch: false));
            Assert.True(added.Succeeded, added.Error?.ToString());
            return added.Value!;
        }

        public IReadOnlyList<ServerEntry> Servers() => Service.ListServers().Value!;

        public (int Code, string Out, string Err) Cli(params string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            string[] common = ["--config-dir", Config, "--projection-dir", Projection];
            var code = Program.Run([.. args, .. common], o, e, new PeClientVersionReader(), new FakeLauncher(),
                TestBackupDir.Variables(_dir), new NeverRunningGameProbe());
            return (code, o.ToString(), e.ToString());
        }

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void Duplicate_CopiesEveryFieldWithANewId()
    {
        using var box = new Box();
        var original = box.AddFull();

        var result = box.Service.DuplicateServer(original.Id);

        Assert.True(result.Succeeded, result.Error?.ToString());
        var copy = result.Value!;
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Matches("^[0-9a-f]{32}$", copy.Id);
        Assert.Equal("Example (copy)", copy.Name);
        Assert.Equal(original with { Id = copy.Id, Name = copy.Name }, copy);
        Assert.Contains(PatchLine, copy.Realmlist, StringComparison.Ordinal);
        Assert.Equal("player1", copy.Account);
        Assert.False(copy.ClearCacheOnSwitch);
        Assert.Equal([original.Id, copy.Id], box.Servers().Select(s => s.Id));
    }

    [Fact]
    public void Duplicate_IsIndependentOfTheOriginal()
    {
        using var box = new Box();
        var original = box.AddFull();
        var copy = box.Service.DuplicateServer(original.Id).Value!;

        var edited = box.Service.UpdateServer(new UpdateServerRequest(copy.Id, "Alt", "other.example.test", "PvE Realm", null, 12340, "alt1",
            ExtraRealmlistLines: []));
        Assert.True(edited.Succeeded, edited.Error?.ToString());
        Assert.Equal(original, box.Servers().Single(s => s.Id == original.Id));

        Assert.True(box.Service.RemoveServer(copy.Id).Succeeded);
        Assert.Equal([original], box.Servers());
    }

    [Fact]
    public void Duplicate_DoesNotBecomeActive()
    {
        using var box = new Box();
        var original = box.Service.AddServer(new NewServer("Example", "logon.example.test")).Value!;
        Assert.True(box.Service.ActivateWithoutClientFiles(original.Id).Succeeded);
        var projection = Directory.GetFiles(box.Projection).ToDictionary(p => p, File.ReadAllBytes);

        var copy = box.Service.DuplicateServer(original.Id).Value!;

        Assert.False(copy.IsActive);
        Assert.Equal(original.Id, box.Servers().Single(s => s.IsActive).Id);
        Assert.Equal(projection.Keys, Directory.GetFiles(box.Projection));
        foreach (var (path, bytes) in projection)
            Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Duplicate_PersistsOnlyTheOriginalsFields_AndNoCredential()
    {
        using var box = new Box();
        var original = box.AddFull();
        var copy = box.Service.DuplicateServer(original.Id).Value!;

        using var json = JsonDocument.Parse(File.ReadAllBytes(box.ServersPath));
        var records = json.RootElement.GetProperty("servers").EnumerateArray().ToList();
        var source = records.Single(r => r.GetProperty("id").GetString() == original.Id);
        var duplicate = records.Single(r => r.GetProperty("id").GetString() == copy.Id);

        var sourceFields = source.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(sourceFields, duplicate.EnumerateObject().Select(p => p.Name));
        foreach (var field in sourceFields.Where(f => f is not "id" and not "name"))
            Assert.Equal(source.GetProperty(field).GetRawText(), duplicate.GetProperty(field).GetRawText());
        Assert.Equal("player1", duplicate.GetProperty("account").GetString());
        Assert.DoesNotContain(sourceFields, f => f.Contains("pass", StringComparison.OrdinalIgnoreCase)
            || f.Contains("token", StringComparison.OrdinalIgnoreCase)
            || f.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || f.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultName_AddsANumberWhenTaken()
    {
        using var box = new Box();
        var original = box.Service.AddServer(new NewServer("Example", "logon.example.test")).Value!;
        Assert.True(box.Service.AddServer(new NewServer("EXAMPLE (COPY)", "other.example.test")).Succeeded);

        Assert.Equal("Example (copy 2)", box.Service.DuplicateServer(original.Id).Value!.Name);
        Assert.Equal("Example (copy 3)", box.Service.DuplicateServer(original.Id).Value!.Name);
    }

    [Fact]
    public void DefaultName_FitsTheNameCapWithoutSplittingTextElements()
    {
        // 62 UTF-16 units of ASCII followed by an emoji (surrogate pair) = 64 units, the maximum.
        var name = new string('a', 62) + "\U0001F600";
        Assert.Equal(Server.MaxNameLength, name.Length);
        var existing = new List<Server> { Server.Create(name, "logon.example.test") };

        var first = PortalService.CreateDuplicateServerName(name, existing);
        Assert.Equal(new string('a', 57) + " (copy)", first);

        existing.Add(Server.Create(first, "logon.example.test"));
        var second = PortalService.CreateDuplicateServerName(name, existing);
        Assert.Equal(new string('a', 55) + " (copy 2)", second);

        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 32));
        var trimmed = PortalService.CreateDuplicateServerName(emoji, []);
        Assert.True(trimmed.Length <= Server.MaxNameLength);
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 28)) + " (copy)", trimmed);
    }

    [Fact]
    public void ExplicitName_IsUsed()
    {
        using var box = new Box();
        var original = box.AddFull();

        var copy = box.Service.DuplicateServer(original.Id, "  Alt account  ").Value!;

        Assert.Equal("Alt account", copy.Name);
        Assert.Equal(original.Host, copy.Host);
    }

    [Theory]
    [InlineData("Example")]
    [InlineData("example")]
    public void ExplicitName_Taken_FailsWithServerNameTaken(string newName)
    {
        using var box = new Box();
        var original = box.Service.AddServer(new NewServer("Example", "logon.example.test")).Value!;
        var before = File.ReadAllBytes(box.ServersPath);

        var result = box.Service.DuplicateServer(original.Id, newName);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.ServerNameTaken, result.Error!.Code);
        Assert.Equal(before, File.ReadAllBytes(box.ServersPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0007name")]
    public void ExplicitName_Invalid_FailsWithInvalidServerInput(string newName)
    {
        using var box = new Box();
        var original = box.Service.AddServer(new NewServer("Example", "logon.example.test")).Value!;
        var before = File.ReadAllBytes(box.ServersPath);

        var result = box.Service.DuplicateServer(original.Id, newName);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.InvalidServerInput, result.Error!.Code);
        Assert.Equal("name", result.Error.Field);
        Assert.Equal(before, File.ReadAllBytes(box.ServersPath));
    }

    [Fact]
    public void ExplicitName_TooLong_FailsWithInvalidServerInput()
    {
        using var box = new Box();
        var original = box.Service.AddServer(new NewServer("Example", "logon.example.test")).Value!;

        var result = box.Service.DuplicateServer(original.Id, new string('a', Server.MaxNameLength + 1));

        Assert.Equal(ServiceErrorCode.InvalidServerInput, result.Error!.Code);
        Assert.Equal("name", result.Error.Field);
        Assert.Single(box.Servers());
    }

    [Fact]
    public void UnknownServer_FailsWithServerNotFound()
    {
        using var box = new Box();
        box.Service.AddServer(new NewServer("Example", "logon.example.test"));

        var result = box.Service.DuplicateServer("0123456789abcdef0123456789abcdef");

        Assert.Equal(ServiceErrorCode.ServerNotFound, result.Error!.Code);
        Assert.Single(box.Servers());
    }

    [Fact]
    public void Cli_DuplicatesByNameWithDefaultOrGivenName()
    {
        using var box = new Box();
        Assert.Equal(0, box.Cli("add", "Example", "logon.example.test", "--realm", "PvP Realm", "--line", PatchLine, "--account", "player1").Code);

        var first = box.Cli("duplicate", "example");
        Assert.Equal(0, first.Code);
        Assert.StartsWith("Added 'Example (copy)' (", first.Out, StringComparison.Ordinal);
        Assert.Contains("as a copy of 'Example'", first.Out, StringComparison.Ordinal);

        Assert.Equal(0, box.Cli("duplicate", "Example", "--name", "Alt").Code);

        var servers = box.Servers();
        Assert.Equal(["Example", "Example (copy)", "Alt"], servers.Select(s => s.Name));
        Assert.All(servers, s =>
        {
            Assert.Equal("PvP Realm", s.Realm);
            Assert.Equal("player1", s.Account);
            Assert.Equal(servers[0].Realmlist, s.Realmlist);
        });
    }

    [Fact]
    public void Cli_Errors()
    {
        using var box = new Box();
        Assert.Equal(0, box.Cli("add", "Example", "logon.example.test").Code);

        var missing = box.Cli("duplicate", "nope");
        Assert.Equal(2, missing.Code);
        Assert.Contains("no server matches 'nope'", missing.Err, StringComparison.Ordinal);

        Assert.Equal(2, box.Cli("duplicate", "Example", "--name", "example").Code);
        Assert.Equal(1, box.Cli("duplicate").Code);
        Assert.Equal(1, box.Cli("duplicate", "Example", "extra").Code);
        Assert.Equal(1, box.Cli("duplicate", "Example", "--name").Code);
        Assert.Single(box.Servers());
    }
}
