using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>#129: the common options work before the command exactly as after it.</summary>
public sealed class CliLeadingOptionsTests
{
    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private sealed class Box : IDisposable
    {
        private readonly TempDirectory _dir = new();

        public Box()
        {
            Directory.CreateDirectory(Path.Combine(Client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        }

        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string Client => _dir.Combine("client");

        public (int Code, string Out, string Err) Run(params string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            var code = Program.Run(args, o, e, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(_dir));
            return (code, o.ToString(), e.ToString());
        }

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void OptionsBeforeCommand_BehaveLikeOptionsAfterIt()
    {
        using var box = new Box();
        var add = box.Run("--config-dir", box.Config, "--projection-dir", box.Projection, "add", "Example", "logon.example.org", "--build", "12340");
        Assert.Equal(0, add.Code);
        Assert.Empty(add.Err);

        var before = box.Run("--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.Client, "list");
        var after = box.Run("list", "--config-dir", box.Config, "--projection-dir", box.Projection, "--client-dir", box.Client);
        var mixed = box.Run("--client-dir", box.Client, "list", "--config-dir", box.Config, "--projection-dir", box.Projection);

        Assert.Equal(0, before.Code);
        Assert.Contains("Example", before.Out);
        Assert.Equal(after, before);
        Assert.Equal(after, mixed);
        Assert.True(File.Exists(Path.Combine(box.Config, "servers.json")));
    }

    [Fact]
    public void OptionBeforeCommand_WithPositionalArguments_AndVerbose()
    {
        using var box = new Box();
        Assert.Equal(0, box.Run("--verbose", "--config-dir", box.Config, "--projection-dir", box.Projection, "add", "Example", "logon.example.org").Code);

        var removed = box.Run("--config-dir", box.Config, "--verbose", "--projection-dir", box.Projection, "remove", "Example");

        Assert.Equal(0, removed.Code);
        Assert.Contains("Removed 'Example'", removed.Out);
    }

    [Fact]
    public void OptionAfterCommand_WinsOverTheSameOptionBeforeIt()
    {
        using var box = new Box();
        var other = box.Config + "-other";
        Assert.Equal(0, box.Run("--config-dir", other, "add", "Example", "logon.example.org", "--config-dir", box.Config, "--projection-dir", box.Projection).Code);

        Assert.True(File.Exists(Path.Combine(box.Config, "servers.json")));
        Assert.False(Directory.Exists(other));
    }

    [Theory]
    [InlineData("--config-dir")]
    [InlineData("--projection-dir")]
    [InlineData("--client-dir")]
    public void LeadingOptionWithoutValue_IsUsageError(string option)
    {
        using var box = new Box();
        var missing = box.Run(option);
        var flagAsValue = box.Run(option, "--verbose", "list");
        var optionAsValue = box.Run(option, "--client-dir", box.Client, "list");

        Assert.Equal(1, missing.Code);
        Assert.Contains($"error: option {option} needs a value", missing.Err);
        Assert.Equal(1, optionAsValue.Code);
        Assert.Contains($"error: option {option} needs a value", optionAsValue.Err);
        Assert.NotEqual(0, flagAsValue.Code);
    }

    [Fact]
    public void UnknownOptionBeforeCommand_IsStillReported()
    {
        using var box = new Box();
        var result = box.Run("--config-dir", box.Config, "--realm", "x", "list");

        Assert.Equal(1, result.Code);
        Assert.Contains("error: unknown option --realm", result.Err);
        Assert.False(Directory.Exists(box.Config));
    }
}
