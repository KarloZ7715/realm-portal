using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>#130: <c>launch --dry-run</c> prints the command a real launch would start, including the configured wrapper.</summary>
public sealed class CliDryRunTests
{
    private sealed class RecordingLauncher : IGameLauncher
    {
        public List<(string Exe, string Dir, string? Wrapper)> Calls { get; } = [];
        public void Launch(string executablePath, string workingDirectory, string? wrapper) => Calls.Add((executablePath, workingDirectory, wrapper));
    }

    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("Not expected.");
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();

        public Box(string clientFolder = "client")
        {
            Client = _dir.Combine(clientFolder);
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            Assert.Equal(0, Run("add", "Example", "logon.example.org").Code);
        }

        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string Client { get; }
        public string Exe => Path.Combine(Client, "Wow.exe");
        public RecordingLauncher Launcher { get; } = new();

        public (int Code, string Out, string Err) Run(params string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            string[] common = ["--config-dir", Config, "--projection-dir", Projection, "--client-dir", Client];
            var code = Program.Run([.. args, .. common], o, e, new PeClientVersionReader(), Launcher, TestBackupDir.Variables(_dir));
            return (code, o.ToString(), e.ToString());
        }

        public void SaveLauncherSettings(LauncherSettings settings) =>
            Assert.True(new PortalService(Config, Projection, new PeClientVersionReader(), new NoLaunch(), getVariable: TestBackupDir.Variables(_dir))
                .SetLauncherSettings(settings).Succeeded);

        public string ClientFile(string relative) => File.ReadAllText(Path.Combine(Client, relative));

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void DryRun_ShowsSavedWrapper_ExactlyAsTheRealLaunchUsesIt()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("launch", "Example", "--wrapper", "gamemoderun").Code);
        var real = Assert.Single(box.Launcher.Calls);

        var dry = box.Run("launch", "Example", "--dry-run");
        var dryActive = box.Run("launch", "--dry-run");

        Assert.Equal(0, dry.Code);
        Assert.Equal($"Dry run: would launch {real.Wrapper} {real.Exe} for 'Example'.{Environment.NewLine}", dry.Out);
        Assert.Equal(dry.Out, dryActive.Out);
        Assert.Equal("gamemoderun", real.Wrapper);
        Assert.Single(box.Launcher.Calls);
    }

    [Fact]
    public void DryRun_WithoutWrapper_ShowsExecutableOnly()
    {
        using var box = new Box();

        var dry = box.Run("launch", "Example", "--dry-run");

        Assert.Equal(0, dry.Code);
        Assert.Equal($"Dry run: would launch {box.Exe} for 'Example'.{Environment.NewLine}", dry.Out);
        Assert.Empty(box.Launcher.Calls);
    }

    [Fact]
    public void DryRun_WrapperOption_OverridesSavedPrefix_WithoutSavingOrSwitching()
    {
        using var box = new Box();
        box.SaveLauncherSettings(new LauncherSettings(Prefix: ["gamemoderun"]));
        var realmlist = Path.Combine("Data", "enUS", "realmlist.wtf");
        Assert.False(File.Exists(Path.Combine(box.Client, realmlist)));

        var dry = box.Run("launch", "Example", "--dry-run", "--wrapper", "wine");
        var again = box.Run("launch", "Example", "--dry-run");

        Assert.Equal($"Dry run: would launch wine {box.Exe} for 'Example'.{Environment.NewLine}", dry.Out);
        Assert.Equal($"Dry run: would launch gamemoderun {box.Exe} for 'Example'.{Environment.NewLine}", again.Out);
        Assert.False(File.Exists(Path.Combine(box.Client, realmlist)));
        Assert.Empty(box.Launcher.Calls);
    }

    [Fact]
    public void DryRun_QuotesPathsWithSpaces()
    {
        using var box = new Box("client folder");
        box.SaveLauncherSettings(new LauncherSettings(Prefix: ["gamemoderun"]));

        var dry = box.Run("launch", "Example", "--dry-run");

        Assert.Equal($"Dry run: would launch gamemoderun \"{box.Exe}\" for 'Example'.{Environment.NewLine}", dry.Out);
    }

    [Theory]
    [MemberData(nameof(UnsupportedSettings))]
    public void DryRun_FailsLikeTheRealLaunch_WhenTheCliLauncherCannotApplyASetting(LauncherSettings settings, string setting)
    {
        using var box = new Box();
        box.SaveLauncherSettings(settings);

        var dry = box.Run("launch", "Example", "--dry-run");
        var real = box.Run("launch", "Example");

        Assert.Equal(2, dry.Code);
        Assert.Equal(2, real.Code);
        Assert.Contains($"Unsupported launcher setting '{setting}'.", dry.Err);
        Assert.Contains($"Unsupported launcher setting '{setting}'.", real.Err);
        Assert.Empty(dry.Out);
        Assert.Empty(box.Launcher.Calls);
    }

    public static TheoryData<LauncherSettings, string> UnsupportedSettings() => new()
    {
        { new LauncherSettings(Arguments: ["-console"]), nameof(GameLaunchSpec.Arguments) },
        { new LauncherSettings(Environment: new Dictionary<string, string?> { ["DXVK_HUD"] = "1" }), nameof(GameLaunchSpec.Environment) },
        { new LauncherSettings(CommandTemplate: "wine {exe}"), nameof(GameLaunchSpec.CommandTemplate) },
        { new LauncherSettings(Prefix: ["env", "A=1"]), nameof(GameLaunchSpec.Prefix) },
    };

    [Theory]
    [InlineData("/games/Wow.exe", "/games/Wow.exe")]
    [InlineData(@"C:\Games\WoW\Wow.exe", @"C:\Games\WoW\Wow.exe")]
    [InlineData("/my games/Wow.exe", "\"/my games/Wow.exe\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("", "\"\"")]
    public void QuoteForDisplay_QuotesOnlyWhenNeeded(string token, string expected) =>
        Assert.Equal(expected, CliApp.QuoteForDisplay(token));
}
