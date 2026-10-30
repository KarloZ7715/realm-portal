using System.Text;
using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>Issue #192 (Core/CLI): extra realmlist lines can be viewed, added, replaced and cleared, with per-line errors.</summary>
public sealed class ExtraRealmlistLinesTests
{
    private const string Host = "logon.example.test";
    private const string PatchA = "set patchlist patch.example.test";
    private const string PatchB = "set patchlist other.example.test";

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

        public string NewClient()
        {
            var client = _dir.Combine("client");
            Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
            Directory.CreateDirectory(Path.Combine(client, "WTF"));
            File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            File.WriteAllText(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist old.example.test\r\n", Encoding.ASCII);
            return client;
        }

        public ServerEntry Single() => Assert.Single(Service.ListServers().Value!);

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

    // ------------------------------------------------------------ RealmlistText

    [Fact]
    public void ExtraLines_RoundTripsThroughBuild()
    {
        var text = RealmlistText.Build(Host, ["  " + PatchA + "\r\n", ""]);

        Assert.Equal($"set realmlist {Host}\r\n{PatchA}\r\n", text);
        Assert.Equal([PatchA], RealmlistText.ExtraLines(text));
        Assert.Equal(text, RealmlistText.Build(Host, RealmlistText.ExtraLines(text)));
        Assert.Empty(RealmlistText.ExtraLines($"set realmlist {Host}\r\n"));
    }

    [Theory]
    [InlineData(new[] { "", "set foo bar" }, 1, 2, InvalidRealmlistKind.ForbiddenKey)]
    [InlineData(new[] { PatchA, "", PatchB }, 2, 3, InvalidRealmlistKind.DuplicateKey)]
    [InlineData(new[] { "set realmlist other.example.test" }, 0, 2, InvalidRealmlistKind.DuplicateKey)]
    [InlineData(new[] { "patchlist patch.example.test" }, 0, 2, InvalidRealmlistKind.MalformedLine)]
    [InlineData(new[] { "set portal portal.example.test" }, 0, 2, InvalidRealmlistKind.ForbiddenKey)]
    [InlineData(new[] { "set patchlist pätch.example.test" }, 0, 2, InvalidRealmlistKind.NotAscii)]
    [InlineData(new[] { "set patchlist a.example.test\nset foo bar" }, 0, 2, InvalidRealmlistKind.BadLineEnding)]
    [InlineData(new[] { "", "set patchlist a.example.test\r\nset patchlist b.example.test" }, 1, 2, InvalidRealmlistKind.BadLineEnding)]
    public void Build_ReportsTheIndexOfTheBadExtraLine(string[] lines, int index, int lineNumber, InvalidRealmlistKind kind)
    {
        var ex = Assert.Throws<InvalidRealmlistException>(() => RealmlistText.Build(Host, lines));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(index, ex.ExtraLineIndex);
        Assert.Equal(lineNumber, ex.LineNumber);
        Assert.Equal(index, ex.ForServer("Example").ExtraLineIndex);
    }

    [Fact]
    public void Build_HostErrorsHaveNoExtraLineIndex()
    {
        var ex = Assert.Throws<InvalidRealmlistException>(() => RealmlistText.Build("bad host", [PatchA]));
        Assert.Null(ex.ExtraLineIndex);
    }

    // ------------------------------------------------------------ service

    [Fact]
    public void Service_AddEditAndRemoveAPatchlistLine()
    {
        using var box = new Box();
        var added = box.Service.AddServer(new NewServer("Example", Host, ExtraRealmlistLines: [PatchA])).Value!;
        Assert.Equal([PatchA], added.ExtraRealmlistLines);

        var kept = box.Service.UpdateServer(new UpdateServerRequest(added.Id, "Example", Host, Realm: "Realm")).Value!;
        Assert.Equal([PatchA], kept.ExtraRealmlistLines);

        var replaced = box.Service.UpdateServer(new UpdateServerRequest(added.Id, "Example", Host, ExtraRealmlistLines: [PatchB])).Value!;
        Assert.Equal([PatchB], replaced.ExtraRealmlistLines);
        Assert.Equal([PatchB], box.Single().ExtraRealmlistLines);

        var cleared = box.Service.UpdateServer(new UpdateServerRequest(added.Id, "Example", Host, ExtraRealmlistLines: [])).Value!;
        Assert.Empty(cleared.ExtraRealmlistLines);
        Assert.Equal($"set realmlist {Host}\r\n", box.Single().Realmlist);
    }

    [Fact]
    public void Service_InvalidLineOnAdd_SavesNothingAndNamesTheLine()
    {
        using var box = new Box();

        var result = box.Service.AddServer(new NewServer("Example", Host, ExtraRealmlistLines: ["", "set foo bar"]));

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.InvalidRealmlist, result.Error!.Code);
        Assert.Equal(InvalidRealmlistKind.ForbiddenKey, result.Error.RealmlistKind);
        Assert.Equal(1, result.Error.ExtraLineIndex);
        Assert.Equal(2, result.Error.LineNumber);
        Assert.Empty(box.Service.ListServers().Value!);
    }

    [Fact]
    public void Service_InvalidLineOnEdit_SavesNothing()
    {
        using var box = new Box();
        var added = box.Service.AddServer(new NewServer("Example", Host, ExtraRealmlistLines: [PatchA])).Value!;
        var before = File.ReadAllBytes(box.ServersPath);

        var result = box.Service.UpdateServer(new UpdateServerRequest(added.Id, "Renamed", Host, ExtraRealmlistLines: [PatchA, PatchB]));

        Assert.Equal(ServiceErrorCode.InvalidRealmlist, result.Error!.Code);
        Assert.Equal(InvalidRealmlistKind.DuplicateKey, result.Error.RealmlistKind);
        Assert.Equal(1, result.Error.ExtraLineIndex);
        Assert.Equal(before, File.ReadAllBytes(box.ServersPath));
    }

    [Fact]
    public void ServiceAndCli_SaveIdenticalDataForTheSameInput()
    {
        using var viaService = new Box();
        using var viaCli = new Box();

        var fromService = viaService.Service.AddServer(new NewServer("Example", Host, "Realm", ExtraRealmlistLines: [PatchA])).Value!;
        Assert.Equal(0, viaCli.Cli("add", "Example", Host, "--realm", "Realm", "--line", PatchA).Code);
        var fromCli = viaCli.Single();
        Assert.Equal(fromService, fromCli with { Id = fromService.Id });

        viaService.Service.UpdateServer(new UpdateServerRequest(fromService.Id, "Example", Host, "Realm", ExtraRealmlistLines: [PatchB]));
        Assert.Equal(0, viaCli.Cli("edit", "Example", "--line", PatchB).Code);
        Assert.Equal(viaService.Single(), viaCli.Single() with { Id = fromService.Id });

        viaService.Service.UpdateServer(new UpdateServerRequest(fromService.Id, "Example", Host, "Realm", ExtraRealmlistLines: []));
        Assert.Equal(0, viaCli.Cli("edit", "Example", "--clear-lines").Code);
        Assert.Equal(viaService.Single(), viaCli.Single() with { Id = fromService.Id });
    }

    // ------------------------------------------------------------ CLI

    [Fact]
    public void Cli_EditReplacesKeepsAndClearsLines_AndListShowsThem()
    {
        using var box = new Box();
        Assert.Equal(0, box.Cli("add", "Example", Host, "--line", PatchA).Code);
        Assert.Contains("build=any  patchlist=patch.example.test", box.Cli("list").Out, StringComparison.Ordinal);

        Assert.Equal(0, box.Cli("edit", "Example", "--realm", "Realm").Code);
        Assert.Equal([PatchA], box.Single().ExtraRealmlistLines);

        Assert.Equal(0, box.Cli("edit", "Example", "--line", PatchB).Code);
        Assert.Equal([PatchB], box.Single().ExtraRealmlistLines);
        Assert.Contains("patchlist=other.example.test", box.Cli("list").Out, StringComparison.Ordinal);

        Assert.Equal(0, box.Cli("edit", "Example", "--clear-lines").Code);
        Assert.Empty(box.Single().ExtraRealmlistLines);
        Assert.DoesNotContain("patchlist", box.Cli("list").Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_InvalidOrConflictingLineOptions_ChangeNothing()
    {
        using var box = new Box();
        Assert.Equal(0, box.Cli("add", "Example", Host, "--line", PatchA).Code);
        var before = File.ReadAllBytes(box.ServersPath);

        var invalid = box.Cli("edit", "Example", "--line", "set foo bar");
        Assert.Equal(2, invalid.Code);
        Assert.Contains("the key \"foo\" is not allowed", invalid.Err, StringComparison.Ordinal);

        var both = box.Cli("edit", "Example", "--line", PatchB, "--clear-lines");
        Assert.Equal(1, both.Code);
        Assert.Contains("use either --line or --clear-lines, not both", both.Err, StringComparison.Ordinal);

        Assert.Equal(1, box.Cli("add", "Other", "other.example.test", "--clear-lines").Code);
        Assert.Equal(1, box.Cli("list", "--clear-lines").Code);
        Assert.Equal(before, File.ReadAllBytes(box.ServersPath));
    }

    [Fact]
    public void Cli_SwitchWritesTheEditedLinesToTheClientRealmlist()
    {
        using var box = new Box();
        var client = box.NewClient();
        var realmlist = Path.Combine(client, "Data", "enUS", "realmlist.wtf");
        Assert.Equal(0, box.Cli("add", "Example", Host, "--line", PatchA).Code);
        Assert.Equal(0, box.Cli("edit", "Example", "--line", PatchB).Code);

        Assert.Equal(0, box.Cli("switch", "Example", "--client-dir", client).Code);
        Assert.Equal($"set realmlist {Host}\r\n{PatchB}\r\n", File.ReadAllText(realmlist, Encoding.ASCII));

        Assert.Equal(0, box.Cli("edit", "Example", "--clear-lines").Code);
        Assert.Equal(0, box.Cli("switch", "Example", "--client-dir", client).Code);
        Assert.Equal($"set realmlist {Host}\r\n", File.ReadAllText(realmlist, Encoding.ASCII));
    }
}
