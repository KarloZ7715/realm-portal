using RealmPortal.Cli;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>CLI add/edit accept a pasted <c>set realmlist …</c> host the same way the App does (#163).</summary>
public class CliPastedRealmlistTests
{
    private sealed class FakeLauncher : IGameLauncher
    {
        public void Launch(string executablePath, string workingDirectory, string? wrapper) { }
    }

    private sealed class Sandbox : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Config => _dir.Combine("config");
        public string Projection => _dir.Combine("projection");
        public string Client => _dir.Combine("client");

        public Sandbox()
        {
            Directory.CreateDirectory(Client);
            File.WriteAllBytes(Path.Combine(Client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        }

        public (int Code, string Out, string Err) Run(string command, params string[] args)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            string[] common = ["--config-dir", Config, "--projection-dir", Projection, "--client-dir", Client];
            var code = Program.Run([command, .. args, .. common], o, e, new PeClientVersionReader(), new FakeLauncher(), TestBackupDir.Variables(_dir), new NeverRunningGameProbe());
            return (code, o.ToString(), e.ToString());
        }

        public string ServersJson => File.ReadAllText(Path.Combine(Config, "servers.json"));

        public void Dispose() => _dir.Dispose();
    }

    public static TheoryData<string, string> AddHostCases => new()
    {
        { "set realmlist logon.example.org", "logon.example.org" },
        { "SET REALMLIST logon.example.org:8085", "logon.example.org:8085" },
        { "set realmlist \"logon.example.org\";", "logon.example.org" },
        { "set realmlist [::1]", "[::1]" },
    };

    [Theory]
    [MemberData(nameof(AddHostCases))]
    public void Add_NormalizesPastedRealmlistHost(string pasted, string expectedHost)
    {
        using var box = new Sandbox();
        var result = box.Run("add", "Pasted", pasted);
        Assert.Equal(0, result.Code);
        Assert.Contains($"\"host\": \"{expectedHost}\"", box.ServersJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("set realmlist bad host")]
    [InlineData("set realmlist host;rm")]
    [InlineData("set realmlist http://evil.example")]
    [InlineData("set realmlist ")]
    [InlineData("setrealmlist host.example")]
    [InlineData("set realmlistx.example")]
    public void Add_RejectsInvalidPastedHost(string pasted)
    {
        using var box = new Sandbox();
        var result = box.Run("add", "Bad", pasted);
        // Service validation (not UsageException) so invalid/empty host keeps exit code 2.
        Assert.Equal(2, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Err));
        Assert.False(File.Exists(Path.Combine(box.Config, "servers.json")));
    }

    [Fact]
    public void Edit_NormalizesPastedRealmlistHost()
    {
        using var box = new Sandbox();
        Assert.Equal(0, box.Run("add", "Alpha", "old.example.org").Code);
        var result = box.Run("edit", "Alpha", "--host", "set realmlist new.example.org:8085");
        Assert.Equal(0, result.Code);
        Assert.Contains("\"host\": \"new.example.org:8085\"", box.ServersJson, StringComparison.Ordinal);
    }
}
