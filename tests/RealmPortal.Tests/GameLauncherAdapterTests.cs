using RealmPortal.Cli;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class GameLauncherAdapterTests
{
    private const string Exe = "/games/wow/Wow.exe";
    private const string Dir = "/games/wow";

    public static IEnumerable<object[]> UnsupportedSettings()
    {
        yield return [new LauncherSettings(CommandTemplate: "wine {exe}"), "CommandTemplate"];
        yield return [new LauncherSettings(Arguments: ["-console"]), "Arguments"];
        yield return [new LauncherSettings(Environment: new Dictionary<string, string?> { ["WINEPREFIX"] = "/p" }), "Environment"];
        yield return [new LauncherSettings(Prefix: ["gamemoderun", "wine"]), "Prefix"];
    }

    [Theory]
    [MemberData(nameof(UnsupportedSettings))]
    public void Play_UnsupportedLauncherSetting_IsInvalidLauncherSettings_AndDoesNotLaunch(LauncherSettings settings, string setting)
    {
        using var dir = new TempDirectory();
        var config = dir.Combine("config");
        var projection = dir.Combine("projection");
        var client = dir.Combine("client");
        var backups = dir.Combine("backups");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(projection);
        Directory.CreateDirectory(backups);
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        File.WriteAllBytes(Path.Combine(client, "Data", "enUS", "realmlist.wtf"), "set realmlist old.example.org\r\n"u8.ToArray());

        var launcher = new RecordingLauncher();
        var service = new PortalService(config, projection, new PeClientVersionReader(), new GameLauncherAdapter(launcher), new NeverRunning(), _ => backups);
        var clientId = service.AddClient(client).Value!.Id;
        var server = service.AddServer(new NewServer("A", "a.example.org")).Value!;
        Assert.True(service.SetLauncherSettings(settings).Succeeded);

        var played = service.Play(new SwitchRequest(clientId, server.Id));

        Assert.False(played.Succeeded);
        Assert.Equal(ServiceErrorCode.InvalidLauncherSettings, played.Error!.Code);
        Assert.Contains($"Unsupported launcher setting '{setting}'", played.Error.Diagnostic);
        Assert.Empty(launcher.Calls);
    }

    [Fact]
    public void Launch_SinglePrefix_IsPassedAsTheWrapper()
    {
        var launcher = new RecordingLauncher();
        var adapter = new GameLauncherAdapter(launcher);

        adapter.Launch(new GameLaunchSpec(Exe, Dir, Prefix: ["wine"]));

        var call = Assert.Single(launcher.Calls);
        Assert.Equal(Exe, call.Exe);
        Assert.Equal(Dir, call.WorkDir);
        Assert.Equal("wine", call.Wrapper);
    }

    private sealed class RecordingLauncher : IGameLauncher
    {
        public List<(string Exe, string WorkDir, string? Wrapper)> Calls { get; } = [];

        public void Launch(string executablePath, string workingDirectory, string? wrapper) =>
            Calls.Add((executablePath, workingDirectory, wrapper));
    }

    private sealed class NeverRunning : IGameProcessProbe
    {
        public bool IsGameRunning(string clientRoot) => false;
    }
}
