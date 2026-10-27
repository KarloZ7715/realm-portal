using System.Text.Json;
using RealmPortal.App.Network;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

public class NetworkSettingsStoreTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var store = new NetworkSettingsStore(temp.Path);
        var loaded = store.Load();
        Assert.True(loaded.UpdatesAutoCheck);
        Assert.True(loaded.StatusProbeEnabled);
        Assert.Null(loaded.UpdatesLastAttemptUtc);
        Assert.Null(loaded.UpdatesSkippedVersion);
    }

    [Fact]
    public void Save_RoundTripsDottedKeys()
    {
        using var temp = new TempDirectory();
        var store = new NetworkSettingsStore(temp.Path);
        var settings = new NetworkSettings
        {
            UpdatesAutoCheck = false,
            StatusProbeEnabled = false,
            UpdatesSkippedVersion = "1.2.0",
            UpdatesLaterVersion = "1.1.5",
            UpdatesLaterUntilUtc = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
            UpdatesEtag = "\"abc\"",
            UpdatesLastAttemptUtc = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
        };
        store.Save(settings);

        var path = Path.Combine(temp.Path, "network-settings.json");
        Assert.True(File.Exists(path));
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.False(root.GetProperty("updates.auto_check").GetBoolean());
        Assert.False(root.GetProperty("status.probe_enabled").GetBoolean());
        Assert.Equal("1.2.0", root.GetProperty("updates.skipped_version").GetString());

        var reloaded = store.Load();
        Assert.False(reloaded.UpdatesAutoCheck);
        Assert.False(reloaded.StatusProbeEnabled);
        Assert.Equal("1.2.0", reloaded.UpdatesSkippedVersion);
        Assert.Equal("1.1.5", reloaded.UpdatesLaterVersion);
        Assert.Equal("\"abc\"", reloaded.UpdatesEtag);
    }

    [Fact]
    public void Load_FutureLastAttempt_ClampedToNow()
    {
        using var temp = new TempDirectory();
        var store = new NetworkSettingsStore(temp.Path);
        store.Save(new NetworkSettings
        {
            UpdatesLastAttemptUtc = DateTimeOffset.UtcNow.AddDays(3),
        });
        var loaded = store.Load();
        Assert.NotNull(loaded.UpdatesLastAttemptUtc);
        Assert.True(loaded.UpdatesLastAttemptUtc <= DateTimeOffset.UtcNow.AddSeconds(2));
    }

    [Fact]
    public void Load_WrongVersion_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "network-settings.json");
        File.WriteAllText(path, """{"version":99,"updates.auto_check":false}""");
        var loaded = new NetworkSettingsStore(temp.Path).Load();
        Assert.True(loaded.UpdatesAutoCheck);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "network-settings.json"), "{not-json");
        var loaded = new NetworkSettingsStore(temp.Path).Load();
        Assert.True(loaded.UpdatesAutoCheck);
        Assert.True(loaded.StatusProbeEnabled);
    }
}
