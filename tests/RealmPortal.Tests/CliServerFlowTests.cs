using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class CliServerFlowTests
{
    private sealed class FakeLauncher : IGameLauncher
    {
        public List<(string Exe, string WorkDir, string? Wrapper)> Calls { get; } = [];

        public void Launch(string executablePath, string workingDirectory, string? wrapper) =>
            Calls.Add((executablePath, workingDirectory, wrapper));
    }

    /// <summary>Temp config, projection and client folders; every command gets them through explicit options.</summary>
    private sealed class Sandbox : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public FakeLauncher Launcher { get; } = new();
        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string Client => _dir.Combine("client");
        public string ActivoPath => ActiveProjection.FilePath(Projection);

        public Func<string, string?> Variables(Func<string, string?>? inner = null) =>
            inner is null ? TestBackupDir.Variables(_dir) : TestBackupDir.Variables(_dir, inner);

        public Sandbox(int? clientBuild = 12340)
        {
            Directory.CreateDirectory(Client);
            if (clientBuild is { } build)
                File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, build)));
        }

        public (int Code, string Out, string Err) Run(string command, params string[] args) =>
            RunWith(true, command, args);

        public (int Code, string Out, string Err) RunWithoutClient(string command, params string[] args) =>
            RunWith(false, command, args);

        private (int Code, string Out, string Err) RunWith(bool withClient, string command, string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            string[] common = ["--config-dir", Config, "--projection-dir", Projection];
            if (withClient)
                common = [.. common, "--client-dir", Client];
            var code = Program.Run([command, .. args, .. common], o, e, new PeClientVersionReader(), Launcher, Variables());
            return (code, o.ToString(), e.ToString());
        }

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void EndToEnd_Add_List_Switch_Undo_Status_Launch()
    {
        using var box = new Sandbox(clientBuild: 12340);

        Assert.Equal(0, box.Run("add", "Wotlk", "wotlk.example.org", "--build", "12340", "--realm", "R1",
            "--line", "set patchlist patch.example.org").Code);
        Assert.Equal(0, box.Run("add", "Classic", "classic.example.org", "--build", "5875").Code);
        Assert.Equal(0, box.Run("add", "Open", "open.example.org").Code);

        // list shows only servers the client can play on
        var list = box.Run("list");
        Assert.Equal(0, list.Code);
        Assert.Contains("Wotlk", list.Out);
        Assert.Contains("Open", list.Out);
        Assert.DoesNotContain("Classic", list.Out);
        Assert.Contains("Classic", box.Run("list", "--all").Out);
        Assert.Contains("[incompatible]", box.Run("list", "--all").Out);

        // switch + activo.json
        var sw = box.Run("switch", "wotlk");
        Assert.Equal(0, sw.Code);
        Assert.Contains("Switched to 'Wotlk'", sw.Out);
        var active = new ServerStore(box.Config, new RealmPortal.Core.IO.AtomicFileWriter()).Find("Wotlk")!;
        Assert.Equal(ActiveProjection.Serialize(active), File.ReadAllBytes(box.ActivoPath));
        Assert.Equal("set realmlist wotlk.example.org\r\nset patchlist patch.example.org\r\n", active.Realmlist);

        // incompatible switch is refused, --force allows it
        var refused = box.Run("switch", "Classic");
        Assert.Equal(2, refused.Code);
        Assert.Contains("requires client build 5875", refused.Err);
        Assert.Equal(ActiveProjection.Serialize(active), File.ReadAllBytes(box.ActivoPath));

        Assert.Equal(0, box.Run("switch", "Open").Code);
        var undo = box.Run("undo");
        Assert.Equal(0, undo.Code);
        Assert.Contains("restored 'Wotlk'", undo.Out);
        Assert.Equal(ActiveProjection.Serialize(active), File.ReadAllBytes(box.ActivoPath));

        var status = box.Run("status");
        Assert.Equal(0, status.Code);
        Assert.Contains("Active server: Wotlk", status.Out);
        Assert.Contains("activo.json: in sync", status.Out);
        Assert.Contains("Client: 12340 (3.3.5.12340) (compatible)", status.Out);

        // launch: dry run launches nothing, the real path goes through the fake launcher only
        var dry = box.Run("launch", "--dry-run");
        Assert.Equal(0, dry.Code);
        Assert.Contains("Dry run: would launch", dry.Out);
        Assert.Empty(box.Launcher.Calls);

        var launch = box.Run("launch", "--wrapper", "wine");
        Assert.Equal(0, launch.Code);
        var call = Assert.Single(box.Launcher.Calls);
        Assert.Equal(box.Client, call.WorkDir);
        Assert.Equal("Wow.exe", Path.GetFileName(call.Exe), ignoreCase: true);
        Assert.Equal("wine", call.Wrapper);
    }

    [Fact]
    public void Add_EscapesControlCharactersInCliErrors()
    {
        using var box = new Sandbox(clientBuild: null);

        var result = box.RunWithoutClient("remove", "bad\u001b[31mname");

        Assert.Equal(2, result.Code);
        Assert.DoesNotContain('\u001b', result.Err);
        Assert.Contains("\\u001B", result.Err);
    }

    [Fact]
    public void Status_MarksBuildMismatch_Incompatible_LikeCompatibilityFilter()
    {
        using var box = new Sandbox(clientBuild: 8606);
        Assert.Equal(0, box.Run("add", "Wotlk", "wotlk.example.org", "--build", "12340").Code);
        Assert.Equal(0, box.Run("switch", "Wotlk", "--force").Code);

        var client = new PeClientVersionReader().Read(box.Client);
        var server = new ServerStore(box.Config, new RealmPortal.Core.IO.AtomicFileWriter()).Find("Wotlk")!;
        Assert.False(CompatibilityFilter.IsCompatible(server, client));

        var status = box.Run("status");
        Assert.Equal(0, status.Code);
        Assert.Contains($"Client: {client} (INCOMPATIBLE)", status.Out);
    }

    [Fact]
    public void Add_WithClientDir_DoesNotRegisterTheClient()
    {
        using var box = new Sandbox();
        var app = new CliApp(new StringWriter(), new StringWriter(), new PeClientVersionReader(), box.Launcher, box.Variables());

        var code = app.Run("add",
        [
            "Wotlk", "wotlk.example.org",
            "--config-dir", box.Config,
            "--projection-dir", box.Projection,
            "--client-dir", box.Client,
        ]);

        Assert.Equal(0, code);
        Assert.NotNull(new ServerStore(box.Config, new RealmPortal.Core.IO.AtomicFileWriter()).Find("Wotlk"));
        Assert.Empty(new ClientStore(box.Config, new RealmPortal.Core.IO.AtomicFileWriter()).List());
    }

    [Fact]
    public void Switch_Force_AllowsIncompatibleServer()
    {
        using var box = new Sandbox(clientBuild: 12340);
        box.Run("add", "Classic", "c.example.org", "--build", "5875");

        Assert.Equal(0, box.Run("switch", "Classic", "--force").Code);
        Assert.True(File.Exists(box.ActivoPath));
    }

    [Fact]
    public void LaunchWithServerArgument_SwitchesFirst_AndRefusesIncompatibleUnlessForced()
    {
        using var box = new Sandbox(clientBuild: 12340);
        box.Run("add", "Wotlk", "w.example.org", "--build", "12340");
        box.Run("add", "Classic", "c.example.org", "--build", "5875");

        Assert.Equal(2, box.Run("launch", "Classic").Code);
        Assert.Empty(box.Launcher.Calls);

        Assert.Equal(0, box.Run("launch", "Wotlk").Code);
        Assert.Single(box.Launcher.Calls);
        Assert.Contains("Active server: Wotlk", box.Run("status").Out);
    }

    [Fact]
    public void Launch_NeedsClientDir_AndAnActiveServer()
    {
        using var box = new Sandbox();
        box.Run("add", "Open", "o.example.org");

        Assert.Equal(1, box.RunWithoutClient("launch").Code);

        var noActive = box.Run("launch");
        Assert.Equal(2, noActive.Code);
        Assert.Contains("no active server", noActive.Err);
        Assert.Empty(box.Launcher.Calls);
    }

    [Fact]
    public void WithoutClientDir_ListShowsAll_AndSwitchWarnsForVersionedServers()
    {
        using var box = new Sandbox(clientBuild: null);
        box.RunWithoutClient("add", "Classic", "c.example.org", "--build", "5875");

        Assert.Contains("Classic", box.RunWithoutClient("list").Out);
        var sw = box.RunWithoutClient("switch", "Classic");
        Assert.Equal(0, sw.Code);
        Assert.Contains("warning", sw.Err);
    }

    [Fact]
    public void UnreadableClient_FailsWithExitCodeTwo()
    {
        using var box = new Sandbox(clientBuild: null); // folder without Wow.exe
        box.RunWithoutClient("add", "Open", "o.example.org");

        var result = box.Run("list");

        Assert.Equal(2, result.Code);
        Assert.StartsWith("error: ", result.Err);
    }

    [Fact]
    public void Remove_ActiveServer_DeactivatesAndDeletesActivoJson()
    {
        using var box = new Sandbox();
        box.Run("add", "Open", "o.example.org");
        box.Run("switch", "Open");
        Assert.True(File.Exists(box.ActivoPath));

        Assert.Equal(0, box.Run("remove", "Open").Code);

        Assert.False(File.Exists(box.ActivoPath));
        Assert.Contains("Active server: none", box.Run("status").Out);
        Assert.Contains("No servers.", box.Run("list").Out);
    }

    [Fact]
    public void Undo_WithNothingToUndo_FailsWithExitCodeTwo()
    {
        using var box = new Sandbox();
        Assert.Equal(2, box.Run("undo").Code);
    }

    [Theory]
    [InlineData("add", "onlyname")]
    [InlineData("add", "n", "h", "--build", "abc")]
    [InlineData("add", "n", "h", "--build", "0")]
    [InlineData("add", "n", "h", "--build", "00")]
    [InlineData("add", "n", "h", "--build", "-5")]
    [InlineData("add", "n", "h", "--build", "+5")]
    [InlineData("add", "n", "h", "--build", "1.5")]
    [InlineData("add", "n", "h", "--build", "99999999999")]
    [InlineData("add", "n", "h", "--build", "")]
    [InlineData("add", "n", "h", "--bogus")]
    [InlineData("add", "n", "h", "--realm")]
    [InlineData("switch")]
    [InlineData("undo", "extra")]
    [InlineData("list", "--force")]
    public void BadUsage_ReturnsExitCodeOne(params string[] args)
    {
        using var box = new Sandbox();
        var result = box.Run(args[0], args[1..]);

        Assert.Equal(1, result.Code);
        Assert.StartsWith("error: ", result.Err);
        Assert.Contains("Usage:", result.Err);
    }

    [Fact]
    public void Add_DuplicateName_Fails_AndUnknownServerFails()
    {
        using var box = new Sandbox();
        box.Run("add", "Open", "o.example.org");

        Assert.Equal(2, box.Run("add", "open", "x.example.org").Code);
        var unknown = box.Run("switch", "nope");
        Assert.Equal(2, unknown.Code);
        Assert.Contains("no server matches 'nope'", unknown.Err);
    }

    [Fact]
    public void Environment_VariablesProvideDirectories()
    {
        using var box = new Sandbox();
        var env = new Dictionary<string, string>
        {
            ["REALM_PORTAL_CONFIG_DIR"] = box.Config,
            ["REALM_PORTAL_PROJECTION_DIR"] = box.Projection,
            ["REALM_PORTAL_CLIENT_DIR"] = box.Client,
        };
        var o = new StringWriter();
        var e = new StringWriter();
        string[][] commands = [["add", "Open", "o.example.org"], ["switch", "Open"], ["status"]];
        foreach (var args in commands)
            Assert.Equal(0, Program.Run(args, o, e, new PeClientVersionReader(), box.Launcher, box.Variables(n => env.GetValueOrDefault(n))));

        Assert.True(File.Exists(box.ActivoPath));
        Assert.Contains("Client: 12340", o.ToString());
    }

    [Theory]
    [InlineData("--line", "set portal p.example.org", "ForbiddenKey")]
    [InlineData("--line", "set patchlist \"x\"", "InvalidValue")]
    [InlineData("--line", "hello", "MalformedLine")]
    public void Add_WithInvalidRealmlistLine_FailsWithMessage_AndStoresNothing(string option, string line, string kind)
    {
        using var box = new Sandbox();

        var result = box.Run("add", "Bad", "h.example.org", option, line);

        Assert.Equal(2, result.Code);
        Assert.Contains("Invalid realmlist", result.Err);
        Assert.Contains($"[{kind}]", result.Err);
        Assert.Contains("No servers.", box.Run("list").Out);
    }

    [Fact]
    public void Add_WithNonAsciiHost_Fails()
    {
        using var box = new Sandbox();

        var result = box.Run("add", "Bad", "h\u00f3st.example.org");

        Assert.Equal(2, result.Code);
        Assert.Contains("[NotAscii]", result.Err);
        Assert.False(File.Exists(new ServerStore(box.Config, new RealmPortal.Core.IO.AtomicFileWriter()).FilePath));
    }

    [Fact]
    public void Switch_ToHandEditedServerWithInvalidRealmlist_FailsAndWritesNothing()
    {
        using var box = new Sandbox();
        box.Run("add", "Good", "g.example.org");
        box.Run("add", "Bad", "b.example.org");
        box.Run("switch", "Good");
        var storePath = new ServerStore(box.Config, new RealmPortal.Core.IO.AtomicFileWriter()).FilePath;
        File.WriteAllText(storePath, File.ReadAllText(storePath).Replace("set realmlist b.example.org", "SET REALMLIST b.example.org"));
        var activoBefore = File.ReadAllBytes(box.ActivoPath);
        var storeBefore = File.ReadAllBytes(storePath);

        var result = box.Run("switch", "Bad");

        Assert.Equal(2, result.Code);
        Assert.Contains("config file is corrupt or unreadable", result.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(activoBefore, File.ReadAllBytes(box.ActivoPath));
        Assert.Equal(storeBefore, File.ReadAllBytes(storePath));
        Assert.Equal(2, box.Run("launch", "Bad").Code);
        Assert.Empty(box.Launcher.Calls);
    }
}
