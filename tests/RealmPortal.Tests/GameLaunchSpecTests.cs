using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class GameLaunchSpecTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private string Exe { get; }
    private string Dir => _temp.Path;

    public GameLaunchSpecTests()
    {
        Exe = _temp.Combine("Wow.exe");
        File.WriteAllText(Exe, "test executable placeholder");
    }

    public void Dispose() => _temp.Dispose();

    private sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessLaunch> Started { get; } = [];

        public void Start(ProcessLaunch launch) => Started.Add(launch);
    }

    [Fact]
    public void Plain_RunsTheExecutableDirectly()
    {
        var launch = new GameLaunchSpec(Exe, Dir).Resolve();

        Assert.Equal(Exe, launch.FileName);
        Assert.Empty(launch.Arguments);
        Assert.Equal(Dir, launch.WorkingDirectory);
        Assert.Empty(launch.Environment);
    }

    [Fact]
    public void Prefix_Template_AndArguments_AreCombinedInOrder()
    {
        var spec = new GameLaunchSpec(Exe, Dir, "wine {exe} -windowed", ["-console", "a b"], Prefix: ["gamemoderun"]);

        var launch = spec.Resolve();

        Assert.Equal("gamemoderun", launch.FileName);
        Assert.Equal(["wine", Exe, "-windowed", "-console", "a b"], launch.Arguments);
    }

    [Fact]
    public void PathsWithSpaces_StayASingleArgument_AndDirIsReplaced()
    {
        var launch = new GameLaunchSpec(Exe, Dir, "wine --workdir {dir} {exe}").Resolve();

        Assert.Equal("wine", launch.FileName);
        Assert.Equal(["--workdir", Dir, Exe], launch.Arguments);
    }

    [Theory]
    [InlineData("wine {exe}", new[] { "wine", "{exe}" })]
    [InlineData("  wine   {exe}  ", new[] { "wine", "{exe}" })]
    [InlineData("env \"A=1 2\" wine {exe}", new[] { "env", "A=1 2", "wine", "{exe}" })]
    [InlineData("env 'A=\"x\"' {exe}", new[] { "env", "A=\"x\"", "{exe}" })]
    [InlineData("w \"say \\\"hi\\\"\" {exe}", new[] { "w", "say \"hi\"", "{exe}" })]
    [InlineData("C:\\Tools\\wine.exe {exe}", new[] { "C:\\Tools\\wine.exe", "{exe}" })]
    [InlineData("a\"b c\"d {exe}", new[] { "ab cd", "{exe}" })]
    [InlineData("'' {exe}", new[] { "", "{exe}" })]
    public void Templates_AreSplitLikeAShellWithoutExpansion(string template, string[] expected) =>
        Assert.Equal(expected, LaunchCommandTemplate.Split(template));

    [Theory]
    [InlineData("wine")]
    [InlineData("wine \"{exe}")]
    public void InvalidTemplates_AreRejected(string template) =>
        Assert.Throws<ArgumentException>(() => new GameLaunchSpec(Exe, Dir, template).Resolve());

    [Fact]
    public void ShellMetacharacters_AreNeverInterpreted()
    {
        var launch = new GameLaunchSpec(Exe, Dir, "wine {exe} ; rm -rf $HOME `x` $(y)").Resolve();

        Assert.Equal(["{exe}", ";", "rm", "-rf", "$HOME", "`x`", "$(y)"], launch.Arguments.Select(a => a == Exe ? "{exe}" : a));
    }

    [Fact]
    public void Environment_IsPassedThrough_AndNullRemoves()
    {
        var launch = new GameLaunchSpec(Exe, Dir, Environment: new Dictionary<string, string?> { ["WINEPREFIX"] = "/p", ["DISPLAY"] = null }).Resolve();

        Assert.Equal("/p", launch.Environment["WINEPREFIX"]);
        Assert.Null(launch.Environment["DISPLAY"]);
        Assert.True(launch.Environment.ContainsKey("DISPLAY"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A=B")]
    public void InvalidEnvironmentNames_AreRejected(string name) =>
        Assert.Throws<ArgumentException>(() => new GameLaunchSpec(Exe, Dir, Environment: new Dictionary<string, string?> { [name] = "x" }).Resolve());

    [Theory]
    [InlineData("LD_PRELOAD")]
    [InlineData("ld_library_path")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    public void DynamicLoaderEnvironment_IsRejected(string name)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new GameLaunchSpec(Exe, Dir, Environment: new Dictionary<string, string?> { [name] = "payload" }).Resolve());

        Assert.Equal(GameLaunchSpec.UnsupportedSettingParameter, error.ParamName);
    }

    [Theory]
    [InlineData("LD_PRELOAD")]
    [InlineData("ld_library_path")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    public void Resolve_PreservesNullRestrictedLoaderEntry(string name)
    {
        var launch = new GameLaunchSpec(Exe, Dir, Environment: new Dictionary<string, string?> { [name] = null }).Resolve();

        Assert.True(launch.Environment.ContainsKey(name));
        Assert.Null(launch.Environment[name]);
    }

    [Fact]
    public void Resolve_ComposesWineLaunchFields()
    {
        var winePrefix = _temp.Combine("wineprefix");
        var spec = new GameLaunchSpec(
            ExecutablePath: Exe,
            WorkingDirectory: Dir,
            CommandTemplate: "wine {exe}",
            Arguments: ["-windowed", "-console"],
            Environment: new Dictionary<string, string?> { ["WINEPREFIX"] = winePrefix, ["LD_PRELOAD"] = null },
            Prefix: ["gamemoderun"]);

        var launch = spec.Resolve();

        Assert.Equal("gamemoderun", launch.FileName);
        Assert.Equal(["wine", Exe, "-windowed", "-console"], launch.Arguments);
        Assert.Equal(Dir, launch.WorkingDirectory);
        Assert.Equal(winePrefix, launch.Environment["WINEPREFIX"]);
        Assert.Null(launch.Environment["LD_PRELOAD"]);
    }

    [Fact]
    public void RelativeOrMissingExecutable_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new GameLaunchSpec("Wow.exe", Dir).Resolve());
        Assert.Throws<ArgumentException>(() => new GameLaunchSpec(_temp.Combine("Missing.exe"), Dir).Resolve());
    }

    [Fact]
    public void EmptyExecutableOrEmptyProgram_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new GameLaunchSpec("", Dir).Resolve());
        Assert.Throws<ArgumentException>(() => new GameLaunchSpec(Exe, "").Resolve());
        Assert.Throws<ArgumentException>(() => new GameLaunchSpec(Exe, Dir, Prefix: [""]).Resolve());
    }

    [Fact]
    public void Launcher_SendsTheResolvedProcessToTheRunner_AndNothingRealStarts()
    {
        var runner = new FakeRunner();
        IGameLaunchService service = new ProcessGameLauncher(runner);

        service.Launch(new GameLaunchSpec(Exe, Dir, "wine {exe}", ["-x"]));

        var started = Assert.Single(runner.Started);
        Assert.Equal("wine", started.FileName);
        Assert.Equal([Exe, "-x"], started.Arguments);
    }

    [Fact]
    public void LegacyLauncher_KeepsItsBehavior()
    {
        var runner = new FakeRunner();
        IGameLauncher launcher = new ProcessGameLauncher(runner);

        launcher.Launch(Exe, Dir, null);
        launcher.Launch(Exe, Dir, "wine");
        launcher.Launch(Exe, Dir, "  ");

        Assert.Equal(Exe, runner.Started[0].FileName);
        Assert.Empty(runner.Started[0].Arguments);
        Assert.Equal("wine", runner.Started[1].FileName);
        Assert.Equal([Exe], runner.Started[1].Arguments);
        Assert.Equal(Exe, runner.Started[2].FileName);
        Assert.All(runner.Started, s => Assert.Equal(Dir, s.WorkingDirectory));
    }

    [Fact]
    public void SystemProcessRunner_AppliesArgumentsAndEnvironment_ToARealHarmlessProcess()
    {
        if (!OperatingSystem.IsLinux())
            return; // uses /bin/sh, which only exists on Linux

        using var dir = new TempDirectory();
        var output = dir.Combine("out.txt");
        var launch = new GameLaunchSpec(
            "/bin/sh", dir.Path, Arguments: ["-c", "printf '%s|%s|%s' \"$FOO\" \"${BAR-unset}\" \"$PWD\" > out.txt"],
            Environment: new Dictionary<string, string?> { ["FOO"] = "1 2", ["BAR"] = null }).Resolve();
        Environment.SetEnvironmentVariable("BAR", "should-be-removed");
        try
        {
            new SystemProcessRunner().Start(launch);
            for (var i = 0; i < 200 && !(File.Exists(output) && new FileInfo(output).Length > 0); i++)
                Thread.Sleep(50);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BAR", null);
        }

        Assert.Equal($"1 2|unset|{Path.GetFullPath(dir.Path)}", File.ReadAllText(output));
    }
}
