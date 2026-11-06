using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RealmPortal.App.Controls;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Network;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>Layout of Settings, the About and Client backups sub-sheets and the server card menu (#198-#201, #210 follow-ups).</summary>
public sealed class SheetLayoutTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private MainWindow? _window;
    private PortalSession? _session;

    public void Dispose()
    {
        _window?.Close();
        Loc.Instance.SetLanguage(Language.Es);
        _temp.Dispose();
    }

    /// <summary>A real session with one synthetic 3.3.5a client and two fictional servers, the first one active.</summary>
    private (MainWindow Window, MainViewModel Main) Open(double width, double height, Language language)
    {
        var client = _temp.Combine("home", "demo", "Games", "World of Warcraft");
        Directory.CreateDirectory(Path.Combine(client, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
        var config = _temp.Combine("config");
        Directory.CreateDirectory(config);
        _session = PortalSession.CreateForPaths(config, _temp.Combine("projection"), _temp.Combine("backups"));
        var session = _session;
        var clientId = session.Service.AddClient(client, "World of Warcraft").Value!.Id;
        var active = session.Service.AddServer(new NewServer("My Server", "logon.example.com", Account: "demo", ClientId: clientId)).Value!;
        session.Service.AddServer(new NewServer("Another Server", "play.example.net", ClientId: clientId));
        session.Service.SwitchServer(new SwitchRequest(clientId, active.Id));
        session.Service.DiscardUndo(clientId);
        session.UiPreferences.Save(new UiPreferences(language == Language.En ? "en" : "es", [clientId], FirstScanCompleted: true));
        Loc.Instance.SetLanguage(language);
        // No network: login-status probes answer at once and the update check does nothing.
        var main = new MainViewModel(session, autoTick: false, probe: new OfflineProbe(), updates: new NullUpdateChecker());
        main.Servers.ReloadFromService();
        _window = new MainWindow(main) { Width = width, Height = height };
        _window.Show();
        Screenshots.Settle();
        return (_window, main);
    }

    private sealed class OfflineProbe : IConnectionProbe
    {
        public Task<ConnectionProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<ConnectionProbeResult>(ProbeResult.Ok);
    }

    private static async Task Pump()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Screenshots.Settle();
    }

    private static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name && c.IsEffectivelyVisible);

    private static Rect BoundsIn(Control control, Visual relativeTo) =>
        new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(720, 440, Language.En)]
    [InlineData(720, 440, Language.Es)]
    [InlineData(1000, 600, Language.En)]
    [InlineData(1400, 1000, Language.En)]
    public void Settings_ScrolledToTheEnd_ShowsTheWholeAboutCard(double width, double height, Language language)
    {
        var (window, main) = Open(width, height, language);
        main.SectionIndex = 2;
        Screenshots.Settle();
        var scroller = Named<ScrollViewer>(window, "Scroller");
        // Twice: a late layout pass (fonts, wrapped text) can still grow the extent after the first jump.
        scroller.ScrollToEnd();
        Screenshots.Settle();
        scroller.ScrollToEnd();
        Screenshots.Settle();

        var button = BoundsIn(Named<Button>(window, "AboutButton"), scroller);
        Assert.True(button.Bottom <= scroller.Viewport.Height, $"About button ends at {button.Bottom}, viewport is {scroller.Viewport.Height}");
        // Only the 12 px bottom margin is left under the last card: no large empty gap.
        var card = Named<Button>(window, "AboutButton").GetVisualAncestors().OfType<ContentControl>().First(c => c.Classes.Contains("card"));
        var gap = scroller.Viewport.Height - BoundsIn(card, scroller).Bottom;
        Assert.InRange(gap, 0, 12.5);
        // The extent covers the whole content, margins included, so there is no hidden overflow below the last card.
        var content = Named<StackPanel>(window, "ScrollContent");
        Assert.Equal(content.Bounds.Height + content.Margin.Top + content.Margin.Bottom, scroller.Extent.Height, 0.5);
        Assert.True(Named<Button>(window, "SaveButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task SubtitleGap_IsTheSameOnClientsBackupsAndAbout()
    {
        var (window, main) = Open(1000, 600, Language.En);
        main.SectionIndex = 1;
        await Pump();
        var clientsGap = GapBelowSubtitle(window, Named<ContentControl>(window.GetVisualDescendants().OfType<Views.ClientsView>().Single(), "ClientsSheet"));

        main.SectionIndex = 2;
        main.Settings.OpenBackupsManagerCommand.Execute(null);
        await Pump();
        var backups = window.GetVisualDescendants().OfType<Views.BackupsView>().Single();
        var backupsGap = GapBelowSubtitle(window, Named<ContentControl>(backups, "ClientCard"));

        main.SectionIndex = 0;
        main.SectionIndex = 2;
        main.Settings.OpenAboutCommand.Execute(null);
        for (var i = 0; i < 100 && main.Content is not AboutViewModel; i++)
        {
            await Task.Delay(20);
        }

        await Pump();
        var about = window.GetVisualDescendants().OfType<Views.AboutView>().Single();
        var aboutGap = GapBelowSubtitle(window, Named<StackPanel>(about, "ScrollContent"));

        Assert.Equal(14, clientsGap, 0.5);
        Assert.Equal(clientsGap, backupsGap, 0.5);
        Assert.Equal(clientsGap, aboutGap, 0.5);
    }

    private static double GapBelowSubtitle(Window window, Control content)
    {
        var subtitle = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Subtitle" && t.IsEffectivelyVisible);
        Assert.False(string.IsNullOrWhiteSpace(subtitle.Text));
        return BoundsIn(content, window).Top - BoundsIn(subtitle, window).Bottom;
    }

    [AvaloniaFact]
    public async Task About_RendersTheLicensesWithoutMarkdownSyntax()
    {
        var (window, main) = Open(1000, 600, Language.En);
        main.SectionIndex = 2;
        main.Settings.OpenAboutCommand.Execute(null);
        for (var i = 0; i < 100 && main.Content is not AboutViewModel; i++)
        {
            await Task.Delay(20);
        }

        await Pump();
        var view = Named<MarkdownView>(window, "ThirdPartyText");
        var texts = view.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? string.Empty)
            .ToList();

        Assert.Contains("Realm Portal — Third-party licenses", texts);
        Assert.Contains("Avalonia", texts);
        Assert.DoesNotContain(texts, t => t.TrimStart().StartsWith('#') || t.TrimStart().StartsWith('|') || t.Contains('`', StringComparison.Ordinal));
        var inner = Named<ScrollViewer>(window, "ThirdPartyScroller");
        Assert.True(inner.Extent.Height > inner.Viewport.Height, "the license text scrolls inside its box");
        inner.ScrollToEnd();
        Screenshots.Settle();
        Assert.Equal(inner.Extent.Height - inner.Viewport.Height, inner.Offset.Y, 0.5);
    }

    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task ServerMenu_HasNoEmptyRowAndShowsTheHintOnlyWhenItApplies(int cardIndex, bool locked)
    {
        var (window, main) = Open(1000, 600, Language.En);
        if (locked)
        {
            main.Servers.All[cardIndex].SetLock(ServerCardLock.NoInUseClient, null);
        }

        var card = window.GetVisualDescendants().OfType<SplitButton>().ElementAt(cardIndex);
        var flyout = (MenuFlyout)card.Flyout!;
        Assert.All(flyout.Items, item => Assert.True(item is MenuItem or Separator, $"{item?.GetType().Name} would get an extra menu row"));

        flyout.ShowAt(card);
        await Pump();
        var presenter = window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().FirstOrDefault()
                        ?? TopLevel.GetTopLevel(card)!.GetVisualDescendants().OfType<MenuFlyoutPresenter>().FirstOrDefault()
                        ?? flyout.Items.OfType<MenuItem>().First().FindAncestorOfType<MenuFlyoutPresenter>()!;
        var rows = presenter.GetVisualDescendants().OfType<MenuItem>().Where(m => m.IsEffectivelyVisible).Select(m => m.Name).ToList();
        string?[] expected = locked
            ? ["MenuPlay", "MenuSwitchOnly", "MenuEdit", "MenuDelete", "MenuDisabledHint"]
            : ["MenuPlay", "MenuSwitchOnly", "MenuEdit", "MenuDelete"];
        Assert.Equal(expected, rows);
        if (locked)
        {
            var hint = flyout.Items.OfType<MenuItem>().Single(i => i.Name == "MenuDisabledHint");
            Assert.Equal(main.Servers.All[cardIndex].MenuDisabledHint, Avalonia.Automation.AutomationProperties.GetName(hint));
        }
        flyout.Hide();
    }

    private void AssertLastControlFullyVisible(ScrollViewer scroller, Control last)
    {
        scroller.ScrollToEnd();
        Screenshots.Settle();
        scroller.ScrollToEnd();
        Screenshots.Settle();
        var content = Named<Control>(scroller, "ScrollContent");
        Assert.Equal(content.Bounds.Height + content.Margin.Top + content.Margin.Bottom, scroller.Extent.Height, 0.5);
        var bottom = BoundsIn(last, scroller).Bottom;
        Assert.True(bottom <= scroller.Viewport.Height + 0.5, $"last control ends at {bottom}, viewport is {scroller.Viewport.Height}");
    }

    [AvaloniaFact]
    public void Clients_ScrolledToTheEnd_ShowsSearchAgainFully()
    {
        // Short window and many clients so the sheet scrolls; ScrollViewer.Padding would cut "Search again".
        var config = _temp.Combine("config-clients");
        Directory.CreateDirectory(config);
        var session = PortalSession.CreateForPaths(config, _temp.Combine("projection-c"), _temp.Combine("backups-c"));
        var ids = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var folder = _temp.Combine("home", "demo", "Games", $"Client-{i}");
            Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            ids.Add(session.Service.AddClient(folder, $"Client {i}").Value!.Id);
        }

        session.UiPreferences.Save(new UiPreferences("en", ids, FirstScanCompleted: true));
        Loc.Instance.SetLanguage(Language.En);
        var main = new MainViewModel(session, autoTick: false, probe: new OfflineProbe(), updates: new NullUpdateChecker());
        _window = new MainWindow(main) { Width = 720, Height = 360 };
        _window.Show();
        Screenshots.Settle();
        main.SectionIndex = 1;
        Screenshots.Settle();
        var scroller = Named<ScrollViewer>(_window, "Scroller");
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "Clients must scroll so the padding bug can show");
        AssertLastControlFullyVisible(scroller, Named<Button>(_window, "RescanButton"));
    }

    [AvaloniaFact]
    public async Task EditClient_ScrolledToTheEnd_ShowsTheFooterButtons()
    {
        var (window, main) = Open(720, 360, Language.En);
        main.SectionIndex = 1;
        await Pump();
        main.Clients.Items[0].EditCommand.Execute(null);
        await Pump();
        AssertLastControlFullyVisible(Named<ScrollViewer>(window, "Scroller"), Named<Button>(window, "SaveButton"));
    }

    [AvaloniaFact]
    public async Task AddServer_ScrolledToTheEnd_ShowsTheFooterButtons()
    {
        var (window, main) = Open(720, 360, Language.En);
        main.Servers.RequestAddServerCommand.Execute(null);
        await Pump();
        var scroller = Named<ScrollViewer>(window, "Scroller");
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "Add server must scroll at this height");
        AssertLastControlFullyVisible(scroller, Named<Button>(window, "SaveButton"));
    }

    [AvaloniaFact]
    public async Task BackupsList_UsesContentMarginInsteadOfScrollViewerPadding()
    {
        var (window, main) = Open(720, 440, Language.En);
        main.SectionIndex = 2;
        Screenshots.Settle();
        var clientId = _session!.Service.ListClients().Value![0].Id;
        for (var i = 0; i < 3; i++)
        {
            var created = _session.Service.CreateBackup(clientId, $"shot-{i}");
            Assert.True(created.Succeeded, created.Error?.Code.ToString());
        }

        main.Settings.OpenBackupsManagerCommand.Execute(null);
        await Pump();
        var scroller = Named<ScrollViewer>(window, "BackupScroller");
        var list = Named<ItemsControl>(window, "BackupList");
        Assert.Equal(new Thickness(0, 0, 10, 10), list.Margin);
        Assert.Equal(default, scroller.Padding);
        // Extent includes the list margin; with ScrollViewer.Padding the bottom gap was outside the extent.
        Assert.Equal(list.Bounds.Height + list.Margin.Top + list.Margin.Bottom, scroller.Extent.Height, 0.5);
        scroller.ScrollToEnd();
        Screenshots.Settle();
        scroller.ScrollToEnd();
        Screenshots.Settle();
        var rows = list.GetVisualDescendants().OfType<ContentControl>().Where(c => c.Classes.Contains("thin")).ToList();
        Assert.NotEmpty(rows);
        var bottom = BoundsIn(rows[^1], scroller).Bottom;
        Assert.True(bottom <= scroller.Viewport.Height + 0.5, $"last backup ends at {bottom}, viewport is {scroller.Viewport.Height}");
    }
}
