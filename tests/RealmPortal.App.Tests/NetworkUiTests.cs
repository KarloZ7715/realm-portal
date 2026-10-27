using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;
using RealmPortal.Core.Network;
using RealmPortal.Core.Service;
using RealmPortal.App.Settings;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Tests;

public class NetworkUiTests
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

    [AvaloniaFact]
    public void Settings_TogglesPersistImmediately()
    {
        Loc.Instance.SetLanguage(Language.En);
        var network = new MemoryNetworkSettingsStore();
        var vm = new SettingsViewModel(new MemorySettings(), network: network, updates: new NullUpdateChecker());

        Assert.True(vm.AutoCheckUpdates);
        Assert.True(vm.StatusProbeEnabled);

        vm.AutoCheckUpdates = false;
        vm.StatusProbeEnabled = false;

        var saved = network.Load();
        Assert.False(saved.UpdatesAutoCheck);
        Assert.False(saved.StatusProbeEnabled);
    }

    [AvaloniaFact]
    public void Settings_StatusProbeToggle_RaisesEvent()
    {
        var network = new MemoryNetworkSettingsStore();
        var vm = new SettingsViewModel(new MemorySettings(), network: network);
        bool? seen = null;
        vm.StatusProbeEnabledChanged += v => seen = v;
        vm.StatusProbeEnabled = false;
        Assert.False(seen);
    }

    [AvaloniaFact]
    public async Task Settings_ManualCheck_ShowsSoftFailureFromNullChecker()
    {
        Loc.Instance.SetLanguage(Language.En);
        var vm = new SettingsViewModel(
            new MemorySettings(),
            network: new MemoryNetworkSettingsStore(),
            updates: new PersistingUpdateChecker(new NullUpdateChecker(), new MemoryNetworkSettingsStore()));

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Get("settings.updates.status.failed"), vm.UpdateCheckStatus);
    }

    [AvaloniaFact]
    public async Task Settings_ManualCheck_PresentsOffer()
    {
        Loc.Instance.SetLanguage(Language.En);
        var offer = new UpdateOffer("1.9.0", "v1.9.0", "Hello notes", null);
        var inner = new RecordingUpdateChecker(_ => UpdateCheckResult.FromOffer(offer));
        var store = new MemoryNetworkSettingsStore();
        var updates = new PersistingUpdateChecker(inner, store);
        var vm = new SettingsViewModel(new MemorySettings(), network: store, updates: updates);

        ConfirmDialogChoice? presented = null;
        vm.PresentUpdateAsync = o =>
        {
            presented = ConfirmDialogChoice.Secondary; // Later
            Assert.Equal("1.9.0", o.Version);
            return Task.FromResult<ConfirmDialogChoice?>(ConfirmDialogChoice.Secondary);
        };

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(ConfirmDialogChoice.Secondary, presented);
        Assert.Equal("1.9.0", store.Load().UpdatesLaterVersion);
        Assert.Null(vm.UpdateCheckStatus);
    }

    [AvaloniaFact]
    public void UpdateNotice_ExposesPlainNotesAndChoices()
    {
        Loc.Instance.SetLanguage(Language.En);
        var offer = new UpdateOffer("2.0.0", "v2.0.0", "Plain notes only", "https://github.com/KarloZ7715/realm-portal/releases/tag/v2.0.0");
        var vm = new UpdateNoticeViewModel(offer);
        Assert.Contains("2.0.0", vm.Title, StringComparison.Ordinal);
        Assert.Equal("Plain notes only", vm.ReleaseNotes);
        Assert.True(vm.CanOpenGitHub);
        Assert.Equal(Loc.Get("update.action.update"), vm.UpdateText);

        vm.LaterCommand.Execute(null);
        Assert.True(vm.Choice.IsCompletedSuccessfully);
        Assert.Equal(ConfirmDialogChoice.Secondary, vm.Choice.Result);
    }

    [AvaloniaFact]
    public void UpdateOffer_ClampsNotesTo20k()
    {
        var longNotes = new string('x', UpdateOffer.MaxReleaseNotesChars + 50);
        Assert.Equal(UpdateOffer.MaxReleaseNotesChars, UpdateOffer.ClampNotes(longNotes).Length);
    }

    [AvaloniaFact]
    public void ServerItem_StatusDot_TooltipAndColors()
    {
        Loc.Instance.SetLanguage(Language.En);
        var card = ServerItemViewModel.FromSeed(
            new RealmPortal.App.Demo.ServerSeed("Demo", "127.0.0.1", 3724, "3.3.5a", null, false, true),
            _ => { },
            _ => { });

        Assert.False(card.ShowStatusDot);
        card.SetStatusDotVisible(true);
        Assert.True(card.ShowStatusDot);
        Assert.True(card.IsStatusNeutral);

        card.ApplyProbeSnapshot(new ServerProbeSnapshot("id", ServerProbeStatus.Online, null, DateTimeOffset.UtcNow));
        Assert.True(card.IsStatusOnline);
        Assert.False(card.IsStatusOffline);
        Assert.Contains("reachable", card.StatusTooltip, StringComparison.OrdinalIgnoreCase);

        card.ApplyProbeSnapshot(new ServerProbeSnapshot("id", ServerProbeStatus.Offline, ProbeScheduler.ReasonTimedOut, DateTimeOffset.UtcNow));
        Assert.True(card.IsStatusOffline);
        Assert.Contains("timed out", card.StatusTooltip, StringComparison.OrdinalIgnoreCase);

        card.ApplyProbeSnapshot(new ServerProbeSnapshot("id", ServerProbeStatus.Unknown, ProbeScheduler.ReasonDns, null));
        Assert.True(card.IsStatusNeutral);
        Assert.Contains("unknown", card.StatusTooltip, StringComparison.OrdinalIgnoreCase);

        card.SetStatusDotVisible(false);
        Assert.False(card.ShowStatusDot);
        Assert.False(card.IsStatusOnline);
    }
}
