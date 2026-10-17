using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Controls;
using RealmPortal.App.Dev;

namespace RealmPortal.App.Tests;

public class ControlTests
{
    private static T Host<T>(T control) where T : Control
    {
        var window = new Window { Width = 600, Height = 400, Content = control };
        window.Show();
        Screenshots.Settle();
        return control;
    }

    private static string? Source(Control control, string name) =>
        control.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == name).Source;

    private static double RingOpacity(Control control) =>
        control.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == "Ring").Opacity;

    [AvaloniaTheory]
    [InlineData("", "button-primary-normal")]
    [InlineData(":pointerover", "button-primary-hover")]
    [InlineData(":pointerover :pressed", "button-primary-pressed")]
    public void PrimaryButtonPicksPlatePerState(string states, string expected)
    {
        var button = Host(new Button { Content = "Label" });
        ForceState.SetStates(button, states);
        Screenshots.Settle();
        Assert.Equal(expected, Source(button, "Plate"));
    }

    [AvaloniaFact]
    public void DisabledButtonUsesDisabledPlate()
    {
        var button = Host(new Button { Content = "Label", IsEnabled = false });
        Assert.Equal("button-primary-disabled", Source(button, "Plate"));
    }

    [AvaloniaTheory]
    [InlineData("", "button-secondary-normal")]
    [InlineData(":pointerover", "button-secondary-hover")]
    [InlineData(":pointerover :pressed", "button-secondary-pressed")]
    public void SecondaryButtonPicksPlatePerState(string states, string expected)
    {
        var button = Host(new Button { Content = "Label", Classes = { "secondary" } });
        ForceState.SetStates(button, states);
        Screenshots.Settle();
        Assert.Equal(expected, Source(button, "Plate"));
    }

    [AvaloniaFact]
    public void FocusRingShowsOnlyForKeyboardFocus()
    {
        var button = Host(new Button { Content = "Label" });
        Assert.Equal(0, RingOpacity(button));
        ForceState.SetStates(button, ":focus-visible");
        Screenshots.Settle();
        Assert.Equal(1, RingOpacity(button));
    }

    [AvaloniaFact]
    public void ParchmentGhostButtonUsesTheTealRing()
    {
        var button = Host(new Button { Content = "Label", Classes = { "ghost", "parchment" } });
        Assert.Equal("focus-ring-parchment", Source(button, "Ring"));
    }

    [AvaloniaFact]
    public void ShadowMarginIsCompensatedSoTheBodyKeepsItsSize()
    {
        var card = Host(new ContentControl { Classes = { "card" }, Width = 300, Height = 100, Content = "x" });
        var sheet = card.GetVisualDescendants().OfType<NineSlice>().First();
        Assert.Equal(12, sheet.ShadowMargin);
        Assert.Equal(300, sheet.Bounds.Width);
        Assert.Equal(100, sheet.Bounds.Height);
    }

    [AvaloniaFact]
    public void SelectedCardUsesTheActiveArt()
    {
        var item = new ListBoxItem { Content = "Server" };
        var list = Host(new ListBox { Classes = { "cards" }, Items = { item } });
        Assert.Equal("panel-sheet-wide", Source(item, "Sheet"));
        list.SelectedItem = item;
        Screenshots.Settle();
        Assert.Equal("panel-active-wide", Source(item, "Sheet"));
    }

    [AvaloniaFact]
    public void SelectedNavItemShowsTheParchmentTab()
    {
        var first = new ListBoxItem { Content = "Servers" };
        var second = new ListBoxItem { Content = "Settings" };
        var list = Host(new ListBox { Classes = { "nav" }, Items = { first, second }, SelectedIndex = 1 });
        Assert.False(first.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == "Tab").IsVisible);
        var tab = second.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == "Tab");
        Assert.True(tab.IsVisible);
        Assert.Equal(10, tab.ShadowMargin);
    }

    [AvaloniaFact]
    public void BadgeUpperCasesItsLabelAndFollowsStatus()
    {
        var badge = Host(new Badge { Text = "No response", Status = ServerStatus.NoResponse });
        Assert.Equal("NO RESPONSE", badge.DisplayText);
        Assert.Contains(":no-response", badge.Classes);
        badge.Status = ServerStatus.Ready;
        Assert.Contains(":ready", badge.Classes);
        Assert.DoesNotContain(":no-response", badge.Classes);
    }

    [AvaloniaTheory]
    [InlineData(ServerStatus.Active, "seal-active")]
    [InlineData(ServerStatus.NoResponse, "seal-no-response")]
    [InlineData(ServerStatus.Inactive, "seal-inactive")]
    public void SealNamesMatchTheAssets(ServerStatus status, string expected) =>
        Assert.Equal(expected, Seal.AssetName(status));

    [AvaloniaTheory]
    [InlineData("button-primary-normal")]
    [InlineData("button-primary-hover")]
    [InlineData("button-primary-pressed")]
    [InlineData("button-primary-disabled")]
    [InlineData("button-secondary-normal")]
    [InlineData("button-secondary-hover")]
    [InlineData("button-secondary-pressed")]
    [InlineData("button-secondary-disabled")]
    [InlineData("focus-ring-leather")]
    [InlineData("focus-ring-parchment")]
    [InlineData("panel-sheet-wide")]
    [InlineData("panel-sheet-medium")]
    [InlineData("panel-active-wide")]
    [InlineData("panel-active-medium")]
    [InlineData("tab-parchment")]
    public void EveryFrameHasA2xFileWithExactlyDoubleThePixels(string name)
    {
        var (normal, normalFactor) = AssetBitmaps.Frame(name, 1.0);
        var (retina, retinaFactor) = AssetBitmaps.Frame(name, 2.0);
        Assert.Equal(1, normalFactor);
        Assert.Equal(2, retinaFactor);
        Assert.Equal(normal.PixelSize.Width * 2, retina.PixelSize.Width);
        Assert.Equal(normal.PixelSize.Height * 2, retina.PixelSize.Height);
    }

    [AvaloniaFact]
    public void HiResArtIsUsedFromScale1_5()
    {
        Assert.Equal(1, AssetBitmaps.Frame("tab-parchment", 1.25).Factor);
        Assert.Equal(2, AssetBitmaps.Frame("tab-parchment", 1.5).Factor);
    }
}
