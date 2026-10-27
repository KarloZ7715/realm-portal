using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;
using RealmPortal.App.Settings;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Service;

namespace RealmPortal.App.Tests;

public class UpdateNotice105UiTests
{
    private sealed class MemorySettings : ISettingsStore
    {
        private SettingsModel _model = new(
            Language.En,
            "wine {exe}",
            [],
            [],
            new Dictionary<string, string?>(),
            "/tmp/backups");

        public SettingsModel Load() => _model;

        public SettingsError? Save(SettingsModel settings)
        {
            _model = settings;
            return null;
        }
    }

    private static UpdateOffer Offer(string version = "1.1.0", string? html = null) =>
        new(version, "v" + version, "Notes", html);

    [Fact]
    public void ReleasePageUrl_RejectsHttpAndForeignHosts()
    {
        Assert.False(ReleasePageUrl.TryGetTrusted("http://github.com/KarloZ7715/realm-portal/releases/tag/v1.1.0", out _));
        Assert.False(ReleasePageUrl.TryGetTrusted("https://evil.example/releases/tag/v1.1.0", out _));
        Assert.False(ReleasePageUrl.TryGetTrusted("https://github.com.evil.example/KarloZ7715/realm-portal/releases/tag/v1.1.0", out _));
        Assert.False(ReleasePageUrl.TryGetTrusted("https://github.com/other/repo/releases/tag/v1.1.0", out _));
        Assert.True(ReleasePageUrl.TryGetTrusted(
            "https://github.com/KarloZ7715/realm-portal/releases/tag/v1.1.0", out var ok));
        Assert.Equal("https://github.com/KarloZ7715/realm-portal/releases/tag/v1.1.0", ok.AbsoluteUri);
    }

    [Fact]
    public void ReleasePageUrl_Resolve_FallsBackToTagUrl()
    {
        var resolved = ReleasePageUrl.Resolve(Offer("1.2.0", html: "https://evil.example/x"));
        Assert.Equal("https://github.com/KarloZ7715/realm-portal/releases/tag/v1.2.0", resolved);
        Assert.True(ReleasePageUrl.TryGetTrusted(resolved, out _));
    }

    [Fact]
    public void UpdateNotice_CanOpenGitHub_UsesAllowlist()
    {
        var trusted = new UpdateNoticeViewModel(Offer("1.1.0",
            "https://github.com/KarloZ7715/realm-portal/releases/tag/v1.1.0"));
        Assert.True(trusted.CanOpenGitHub);

        var untrustedHtml = new UpdateNoticeViewModel(Offer("1.1.0", "https://evil.example/x"));
        Assert.True(untrustedHtml.CanOpenGitHub); // falls back to constructed tag URL
        Assert.StartsWith(ReleasePageUrl.TagPrefix, untrustedHtml.ResolvedGitHubUrl);
    }

    [Fact]
    public void MemoryStore_Update_PreservesUnrelatedFields()
    {
        var store = new MemoryNetworkSettingsStore();
        store.Save(NetworkSettings.Default with
        {
            StatusProbeEnabled = true,
            UpdatesEtag = "etag-1",
            UpdatesSkippedVersion = "1.0.0",
        });
        var updated = store.Update(s => s with { UpdatesAutoCheck = false });
        Assert.True(updated.Succeeded);
        Assert.False(updated.Value!.UpdatesAutoCheck);
        Assert.True(updated.Value.StatusProbeEnabled);
        Assert.Equal("etag-1", updated.Value.UpdatesEtag);
        Assert.Equal("1.0.0", updated.Value.UpdatesSkippedVersion);
    }

    [AvaloniaFact]
    public void Settings_PersistNetwork_UsesUpdateNotFullSnapshot()
    {
        Loc.Instance.SetLanguage(Language.En);
        var store = new MemoryNetworkSettingsStore();
        store.Save(NetworkSettings.Default with
        {
            StatusProbeEnabled = true,
            UpdatesEtag = "keep",
            UpdatesLastAttemptUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        });
        var vm = new SettingsViewModel(new MemorySettings(), network: store, updates: new NullUpdateChecker());
        Assert.True(vm.StatusProbeEnabled);
        vm.AutoCheckUpdates = false;
        var saved = store.Load().Value!;
        Assert.False(saved.UpdatesAutoCheck);
        Assert.True(saved.StatusProbeEnabled);
        Assert.Equal("keep", saved.UpdatesEtag);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), saved.UpdatesLastAttemptUtc);
    }
}
