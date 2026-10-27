using RealmPortal.App.Network;

namespace RealmPortal.App.Tests;

public class PersistingUpdateCheckerTests
{
    private static UpdateOffer Offer(string version = "1.1.0") =>
        new(version, "v" + version, "Notes for " + version, "https://github.com/KarloZ7715/realm-portal/releases/tag/v" + version);

    [Fact]
    public async Task Automatic_Disabled_ReturnsNoneWithoutCallingInner()
    {
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.FromOffer(Offer()));
        var store = new MemoryNetworkSettingsStore();
        store.Save(new NetworkSettings { UpdatesAutoCheck = false });
        var checker = new PersistingUpdateChecker(inner, store);

        var result = await checker.CheckAsync(UpdateCheckKind.Automatic);
        Assert.Equal(UpdateCheckResultKind.None, result.Kind);
        Assert.Empty(inner.Checks);
    }

    [Fact]
    public async Task Automatic_Within24h_ReturnsNone()
    {
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.FromOffer(Offer()));
        var store = new MemoryNetworkSettingsStore();
        store.Save(new NetworkSettings
        {
            UpdatesAutoCheck = true,
            UpdatesLastAttemptUtc = DateTimeOffset.UtcNow.AddHours(-1),
        });
        var checker = new PersistingUpdateChecker(inner, store);

        var result = await checker.CheckAsync(UpdateCheckKind.Automatic);
        Assert.Equal(UpdateCheckResultKind.None, result.Kind);
        Assert.Empty(inner.Checks);
    }

    [Fact]
    public async Task Automatic_WritesLastAttemptAndOffers()
    {
        var offer = Offer("1.2.0");
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.FromOffer(offer));
        var store = new MemoryNetworkSettingsStore();
        store.Save(new NetworkSettings { UpdatesAutoCheck = true, UpdatesLastAttemptUtc = null });
        var checker = new PersistingUpdateChecker(inner, store);

        var result = await checker.CheckAsync(UpdateCheckKind.Automatic);
        Assert.Equal(UpdateCheckResultKind.Offer, result.Kind);
        Assert.Equal("1.2.0", result.Offer!.Version);
        Assert.NotNull(store.Load().UpdatesLastAttemptUtc);
        Assert.Single(inner.Checks);
        Assert.Equal(UpdateCheckKind.Automatic, inner.Checks[0].Kind);
    }

    [Fact]
    public async Task Manual_WorksWhenAutoCheckDisabled()
    {
        var offer = Offer();
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.FromOffer(offer));
        var store = new MemoryNetworkSettingsStore();
        store.Save(new NetworkSettings { UpdatesAutoCheck = false });
        var checker = new PersistingUpdateChecker(inner, store);

        var result = await checker.CheckAsync(UpdateCheckKind.Manual);
        Assert.Equal(UpdateCheckResultKind.Offer, result.Kind);
    }

    [Fact]
    public async Task Manual_Debounced_ReturnsRateLimited()
    {
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.UpToDate);
        var store = new MemoryNetworkSettingsStore();
        var checker = new PersistingUpdateChecker(inner, store);

        Assert.Equal(UpdateCheckResultKind.UpToDate, (await checker.CheckAsync(UpdateCheckKind.Manual)).Kind);
        Assert.Equal(UpdateCheckResultKind.RateLimited, (await checker.CheckAsync(UpdateCheckKind.Manual)).Kind);
        Assert.Single(inner.Checks);
    }

    [Fact]
    public async Task SkippedVersion_IsNotOffered()
    {
        var offer = Offer("1.3.0");
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.FromOffer(offer));
        var store = new MemoryNetworkSettingsStore();
        store.Save(new NetworkSettings { UpdatesSkippedVersion = "1.3.0", UpdatesLastAttemptUtc = null });
        var checker = new PersistingUpdateChecker(inner, store);

        var auto = await checker.CheckAsync(UpdateCheckKind.Automatic);
        Assert.Equal(UpdateCheckResultKind.None, auto.Kind);

        // Reset debounce / last attempt for manual
        store.Save(new NetworkSettings { UpdatesSkippedVersion = "1.3.0", UpdatesLastAttemptUtc = DateTimeOffset.UtcNow });
        var checker2 = new PersistingUpdateChecker(inner, store);
        var manual = await checker2.CheckAsync(UpdateCheckKind.Manual);
        Assert.Equal(UpdateCheckResultKind.UpToDate, manual.Kind);
    }

    [Fact]
    public void RemindLater_PersistsThreeDayPostpone()
    {
        var offer = Offer("1.4.0");
        var store = new MemoryNetworkSettingsStore();
        var checker = new PersistingUpdateChecker(new NullUpdateChecker(), store);
        var before = DateTimeOffset.UtcNow;
        checker.RemindLater(offer);
        var saved = store.Load();
        Assert.Equal("1.4.0", saved.UpdatesLaterVersion);
        Assert.NotNull(saved.UpdatesLaterUntilUtc);
        Assert.True(saved.UpdatesLaterUntilUtc >= before.AddDays(3).AddSeconds(-2));
        Assert.True(saved.UpdatesLaterUntilUtc <= before.AddDays(3).AddSeconds(5));
    }

    [Fact]
    public void SkipThisVersion_PersistsSkipped()
    {
        var store = new MemoryNetworkSettingsStore();
        var checker = new PersistingUpdateChecker(new NullUpdateChecker(), store);
        checker.SkipThisVersion(Offer("1.5.0"));
        Assert.Equal("1.5.0", store.Load().UpdatesSkippedVersion);
    }

    [Fact]
    public async Task LaterPostpone_SuppressesSameVersion()
    {
        var offer = Offer("1.6.0");
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.FromOffer(offer));
        var store = new MemoryNetworkSettingsStore();
        store.Save(new NetworkSettings
        {
            UpdatesLaterVersion = "1.6.0",
            UpdatesLaterUntilUtc = DateTimeOffset.UtcNow.AddDays(1),
            UpdatesLastAttemptUtc = null,
        });
        var checker = new PersistingUpdateChecker(inner, store);
        var result = await checker.CheckAsync(UpdateCheckKind.Automatic);
        Assert.Equal(UpdateCheckResultKind.None, result.Kind);
    }
}
