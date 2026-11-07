using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;
using RealmPortal.App.Platform;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Network;
using RealmPortal.Core.Service;
using RealmPortal.Core.Updates;
using RealmPortal.Tests.Support;
using AppChecker = RealmPortal.App.Network.IUpdateChecker;
using AppOffer = RealmPortal.App.Network.UpdateOffer;
using AppResult = RealmPortal.App.Network.UpdateCheckResult;

namespace RealmPortal.App.Tests;

/// <summary>
/// Signature failures (#116) on both update surfaces: the full warning stays readable at the smallest window size, and
/// both surfaces offer the release page (and nothing else) as the way forward.
/// </summary>
public class UpdateSignatureFailureUiTests
{
    private static readonly AppOffer Offer = new("1.2.1", "v1.2.1", "Synthetic release", null);
    private const string ReleasePage = "https://github.com/KarloZ7715/realm-portal/releases/tag/v1.2.1";

    private sealed class Online : IConnectionProbe
    {
        public Task<ConnectionProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<ConnectionProbeResult>(ProbeResult.Ok);
    }

    private sealed class FailingInstall(UpdateErrorCode code) : AppChecker
    {
        // Off until the test asks, so the startup check does not open a notice of its own.
        public bool OfferNext { get; set; }

        public Task<AppResult> CheckAsync(UpdateCheckKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(OfferNext ? AppResult.FromOffer(Offer) : AppResult.UpToDate);

        public Task<UpdateInstallOutcome?> InstallUpdateAsync(AppOffer offer, CancellationToken cancellationToken = default) =>
            throw new UpdateException(code, "signature check failed");

        public void RemindLater(AppOffer offer) { }
        public void SkipThisVersion(AppOffer offer) { }
    }

    private sealed class RecordingOpener : IUriOpener
    {
        public List<Uri> Opened { get; } = [];

        public Task<bool> OpenAsync(Uri uri)
        {
            Opened.Add(uri);
            return Task.FromResult(true);
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Rig(UpdateErrorCode code, Language language, int width, int height)
        {
            var config = _temp.Combine("config");
            Directory.CreateDirectory(config);
            var session = PortalSession.CreateForPaths(config, _temp.Combine("projection"), _temp.Combine("backups"));
            var client = _temp.Combine("synthetic-client");
            Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            var id = session.Service.AddClient(client, "Synthetic").Value!.Id;
            session.UiPreferences.Save(new UiPreferences(language == Language.Es ? "es" : "en", [id], FirstScanCompleted: true));
            Checker = new FailingInstall(code);
            Main = new MainViewModel(session, autoTick: false, probe: new Online(), uriOpener: Opener,
                updates: Checker, network: new MemoryNetworkSettingsStore());
            Loc.Instance.SetLanguage(language);
            Window = new MainWindow(Main) { Width = width, Height = height };
            Window.Show();
            Screenshots.Settle();
        }

        public RecordingOpener Opener { get; } = new();
        public FailingInstall Checker { get; }
        public MainViewModel Main { get; }
        public MainWindow Window { get; }

        public void Dispose()
        {
            Window.Close();
            Loc.Instance.SetLanguage(Language.En);
            _temp.Dispose();
        }
    }

    private static async Task Wait(Func<bool> ready, string what)
    {
        for (var i = 0; i < 500 && !ready(); i++)
        {
            await Task.Delay(10);
            Screenshots.Settle();
        }

        Assert.True(ready(), what);
    }

    private static string? ShotDirectory => Environment.GetEnvironmentVariable("RP_SCREENSHOT_DIR");

    [AvaloniaTheory]
    [InlineData(UpdateErrorCode.SignatureMissing, Language.En)]
    [InlineData(UpdateErrorCode.SignatureInvalid, Language.En)]
    [InlineData(UpdateErrorCode.SignatureMissing, Language.Es)]
    [InlineData(UpdateErrorCode.SignatureInvalid, Language.Es)]
    public async Task MainDialog_OffersTheReleasePage(UpdateErrorCode code, Language language)
    {
        using var rig = new Rig(code, language, 720, 440);
        var work = (Task)typeof(MainViewModel)
            .GetMethod("PresentOrDeferUpdateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(rig.Main, [Offer])!;
        await Wait(() => rig.Main.UpdateNotice is not null, "update notice");
        rig.Main.UpdateNotice!.UpdateCommand.Execute(null);
        await Wait(() => rig.Main.Dialog is not null, "failure dialog");

        var dialog = rig.Main.Dialog!;
        Assert.Equal(Loc.Get(UpdateInstallPresentation.FailureBodyKey(new UpdateException(code, "x"))), dialog.Body);
        Assert.Equal(Loc.Get("update.install.open_release_page"), dialog.ConfirmText);
        Assert.False(dialog.HasSecondaryAction);
        if (ShotDirectory is not null)
            Screenshots.Save(rig.Window, 1, $"main-{code}-{language}-720x440.png");

        Assert.Empty(rig.Opener.Opened);
        dialog.ConfirmCommand.Execute(null);
        await work;
        Assert.Equal(ReleasePage, Assert.Single(rig.Opener.Opened).AbsoluteUri);
    }

    [AvaloniaFact]
    public async Task MainDialog_ClosingDoesNotOpenAnything()
    {
        using var rig = new Rig(UpdateErrorCode.SignatureInvalid, Language.En, 720, 440);
        var work = (Task)typeof(MainViewModel)
            .GetMethod("PresentOrDeferUpdateAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(rig.Main, [Offer])!;
        await Wait(() => rig.Main.UpdateNotice is not null, "update notice");
        rig.Main.UpdateNotice!.UpdateCommand.Execute(null);
        await Wait(() => rig.Main.Dialog is not null, "failure dialog");
        rig.Main.Dialog!.CancelCommand.Execute(null);
        await work;
        Assert.Empty(rig.Opener.Opened);
    }

    [AvaloniaTheory]
    [InlineData(UpdateErrorCode.SignatureMissing, Language.En)]
    [InlineData(UpdateErrorCode.SignatureInvalid, Language.En)]
    [InlineData(UpdateErrorCode.SignatureMissing, Language.Es)]
    [InlineData(UpdateErrorCode.SignatureInvalid, Language.Es)]
    public async Task Settings_ShowsTheWholeWarningAndTheReleasePageButton(UpdateErrorCode code, Language language)
    {
        using var rig = new Rig(code, language, 720, 440);
        rig.Main.SectionIndex = 2;
        Screenshots.Settle();
        rig.Checker.OfferNext = true;
        var check = rig.Main.Settings.CheckForUpdatesCommand.ExecuteAsync(null);
        await Wait(() => rig.Main.UpdateNotice is not null, "settings notice");
        rig.Main.UpdateNotice!.UpdateCommand.Execute(null);
        var expected = Loc.Get(UpdateInstallPresentation.FailureBodyKey(new UpdateException(code, "x")));
        await Wait(() => rig.Main.Settings.UpdateCheckStatus == expected, "failure text");
        await check;
        Assert.Equal(ReleasePage, rig.Main.Settings.ReleasePageUrl);

        var scroller = rig.Window.GetVisualDescendants().OfType<ScrollViewer>()
            .First(x => x.Name == "Scroller" && x.IsEffectivelyVisible);
        var status = rig.Window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name == "UpdateCheckStatus" && t.IsEffectivelyVisible);
        var button = rig.Window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Name == "OpenReleasePageButton");
        Screenshots.Settle();
        var buttonTop = button.TranslatePoint(new Point(0, 0), scroller)!.Value.Y;
        scroller.Offset = new Vector(0, scroller.Offset.Y + buttonTop + button.Bounds.Height + 24 - scroller.Viewport.Height);
        Screenshots.Settle();
        Assert.True(button.IsEffectivelyVisible);

        // Wrapped onto several lines, laid out at the size it needs, and inside the visible width of the page.
        Assert.Equal(expected, status.Text);
        Assert.True(status.TextLayout.TextLines.Count > 1, "status should wrap at 720 px");
        Assert.True(status.TextLayout.Width <= status.Bounds.Width + 0.5, "text wider than its box");
        Assert.True(status.Bounds.Height + 0.5 >= status.TextLayout.Height, "text taller than its box");
        var topLeft = status.TranslatePoint(new Point(0, 0), scroller)!.Value;
        Assert.True(topLeft.X >= 0 && topLeft.X + status.Bounds.Width <= scroller.Viewport.Width + 0.5,
            $"status runs past the page: x={topLeft.X} w={status.Bounds.Width} viewport={scroller.Viewport.Width}");
        if (ShotDirectory is not null)
            Screenshots.Save(rig.Window, 1, $"settings-{code}-{language}-720x440.png");

        rig.Main.Settings.OpenReleasePageCommand.Execute(null);
        Assert.Equal(ReleasePage, Assert.Single(rig.Opener.Opened).AbsoluteUri);
    }

    [Fact]
    public void Settings_ReleasePageButton_GoesAwayWithTheMessage()
    {
        var vm = new MainViewModel(autoTick: false).Settings;
        vm.UpdateCheckStatus = "x";
        vm.ReleasePageUrl = ReleasePage;
        vm.UpdateCheckStatus = null;
        Assert.Null(vm.ReleasePageUrl);
    }
}
