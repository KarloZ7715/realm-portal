using System.Text.Json;
using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class NetworkSettingsStoreTests
{
    [Fact]
    public void MissingFile_ReturnsDefaults_BothOn()
    {
        using var env = new TempConfig();
        var store = new NetworkSettingsStore(env.Config, new AtomicFileWriter(), env.Lock);
        var settings = store.Get();
        Assert.True(settings.UpdatesAutoCheck);
        Assert.True(settings.StatusProbeEnabled);
        Assert.Null(settings.UpdatesEtag);
    }

    [Fact]
    public void RoundTrip_PersistsFullKeySet_WithPrivateMode()
    {
        using var env = new TempConfig();
        var store = new NetworkSettingsStore(env.Config, new AtomicFileWriter(), env.Lock);
        var when = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
        store.Set(new NetworkSettings(
            UpdatesAutoCheck: false,
            UpdatesLastAttemptUtc: when,
            UpdatesEtag: "\"abc\"",
            UpdatesSkippedVersion: "1.2.3",
            UpdatesLaterVersion: "1.2.4",
            UpdatesLaterUntilUtc: when.AddDays(3),
            StatusProbeEnabled: false));

        var path = Path.Combine(env.Config, "network-settings.json");
        Assert.True(File.Exists(path));
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.False(root.GetProperty("updates").GetProperty("auto_check").GetBoolean());
        Assert.False(root.GetProperty("status").GetProperty("probe_enabled").GetBoolean());
        Assert.Equal("\"abc\"", root.GetProperty("updates").GetProperty("etag").GetString());

        var again = new NetworkSettingsStore(env.Config, new AtomicFileWriter(), env.Lock).Get();
        Assert.False(again.UpdatesAutoCheck);
        Assert.False(again.StatusProbeEnabled);
        Assert.Equal("1.2.3", again.UpdatesSkippedVersion);
        Assert.Equal("1.2.4", again.UpdatesLaterVersion);
        Assert.Equal("\"abc\"", again.UpdatesEtag);
        Assert.Equal(when, again.UpdatesLastAttemptUtc);
        Assert.Equal(when.AddDays(3), again.UpdatesLaterUntilUtc);
    }

    [Fact]
    public void PortalService_GetSetNetworkSettings()
    {
        using var env = new ServiceEnv();
        var got = env.Service.GetNetworkSettings();
        Assert.True(got.Succeeded);
        Assert.True(got.Value!.StatusProbeEnabled);

        var set = env.Service.SetNetworkSettings(got.Value with { StatusProbeEnabled = false, UpdatesAutoCheck = false });
        Assert.True(set.Succeeded);
        var again = env.Service.GetNetworkSettings().Value!;
        Assert.False(again.StatusProbeEnabled);
        Assert.False(again.UpdatesAutoCheck);
    }

    private sealed class TempConfig : IDisposable
    {
        private readonly TempDirectory _temp = new();
        public string Config { get; }
        public ConfigDirectoryLock Lock { get; }

        public TempConfig()
        {
            Config = _temp.Combine("config");
            Directory.CreateDirectory(Config);
            Lock = new ConfigDirectoryLock(Config);
        }

        public void Dispose() => _temp.Dispose();
    }

    private sealed class ServiceEnv : IDisposable
    {
        private readonly TempDirectory _temp = new();
        public PortalService Service { get; }

        public ServiceEnv()
        {
            var config = _temp.Combine("config");
            var projection = _temp.Combine("projection");
            var backups = _temp.Combine("backups");
            Directory.CreateDirectory(config);
            Directory.CreateDirectory(projection);
            Directory.CreateDirectory(backups);
            Service = new PortalService(
                config,
                projection,
                new RealmPortal.Core.Clients.PeClientVersionReader(),
                new NoLaunch(),
                new NeverRunningGameProbe(),
                _ => backups);
        }

        public void Dispose() => _temp.Dispose();
    }

    private sealed class NoLaunch : RealmPortal.Core.Launch.IGameLaunchService
    {
        public void Launch(RealmPortal.Core.Launch.GameLaunchSpec spec) { }
    }
}
