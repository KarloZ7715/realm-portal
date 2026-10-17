using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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
    public void LeatherSurfaceKeepsLeatherPlatesAndFocusRing()
    {
        var primary = Host(new Button { Content = "Save" });
        Assert.Equal("button-primary-normal", Source(primary, "Plate"));
        Assert.Equal("focus-ring-leather", Source(primary, "Ring"));
        ButtonSurface.SetSurface(primary, ButtonSurfaceKind.Leather);
        Screenshots.Settle();
        Assert.Equal("button-primary-normal", Source(primary, "Plate"));
        var secondary = Host(new Button { Content = "Cancel", Classes = { "secondary" } });
        Assert.Equal("button-secondary-normal", Source(secondary, "Plate"));
        var text = Host(new Button { Content = "Label", Classes = { "ghost" } });
        Assert.False(text.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == "Plate").IsVisible);
    }

    [AvaloniaFact]
    public void ParchmentSurfaceUsesDarkBronzePlatesAndParchmentFocusRing()
    {
        var primary = Host(new Button { Content = "Discard" });
        ButtonSurface.SetSurface(primary, ButtonSurfaceKind.Parchment);
        Screenshots.Settle();
        Assert.Equal("button-primary-parchment-normal", Source(primary, "Plate"));
        Assert.Equal("focus-ring-parchment", Source(primary, "Ring"));
        var secondary = Host(new Button { Content = "Cancel", Classes = { "secondary" } });
        ButtonSurface.SetSurface(secondary, ButtonSurfaceKind.Parchment);
        Screenshots.Settle();
        Assert.Equal("button-secondary-parchment-normal", Source(secondary, "Plate"));
        Assert.Equal("focus-ring-parchment", Source(secondary, "Ring"));
        var text = Host(new Button { Content = "Label", Classes = { "ghost" } });
        ButtonSurface.SetSurface(text, ButtonSurfaceKind.Parchment);
        Screenshots.Settle();
        Assert.Equal("ghost-parchment-normal", Source(text, "Plate"));
        Assert.Equal("focus-ring-parchment", Source(text, "Ring"));
    }

    [AvaloniaFact]
    public void ThreadStitchDividerMatchesTheDesignerSpec()
    {
        var line = Host(new Avalonia.Controls.Shapes.Line { Classes = { "stitch" }, StartPoint = new(0, 0), EndPoint = new(200, 0) });
        Assert.Equal(1.5, line.StrokeThickness);
        Assert.Equal(new[] { 6d, 4d }, line.StrokeDashArray);
        var stroke = Assert.IsType<SolidColorBrush>(line.Stroke);
        Assert.Equal(Color.Parse("#8A6C4A"), stroke.Color);
    }

    [AvaloniaFact]
    public void ShadowLayerOverflowsSoTheBodyKeepsItsSize()
    {
        var card = Host(new ContentControl { Classes = { "card" }, Width = 300, Height = 100, Content = "x" });
        var layers = card.GetVisualDescendants().OfType<NineSlice>().ToList();
        var shadow = layers.Single(n => n.Source == "shadow-soft");
        Assert.Equal(12, shadow.ShadowMargin);
        var sheet = layers.Single(n => n.Name == "Sheet");
        Assert.Equal(0, sheet.ShadowMargin);
        Assert.Equal(300, sheet.Bounds.Width);
        Assert.Equal(100, sheet.Bounds.Height);
        Assert.True(sheet.TileX && sheet.TileY);
    }

    [AvaloniaFact]
    public void ThinCardUsesTheStretchedThinArt()
    {
        var card = Host(new ContentControl { Classes = { "card", "thin" }, Width = 300, Height = 64, Content = "x" });
        var sheet = card.GetVisualDescendants().OfType<NineSlice>().Single(n => n.Name == "Sheet");
        Assert.Equal("panel-thin-normal", sheet.Source);
        Assert.False(sheet.TileX || sheet.TileY);
    }

    [AvaloniaFact]
    public void CardRowPicksArtPerState()
    {
        var item = new ListBoxItem { Content = "Server" };
        var list = Host(new ListBox { Classes = { "cards" }, Items = { item } });
        Assert.Equal("panel-thin-normal", Source(item, "Sheet"));
        ForceState.SetStates(item, ":pointerover");
        Screenshots.Settle();
        Assert.Equal("panel-thin-hover", Source(item, "Sheet"));
        list.SelectedItem = item;
        Screenshots.Settle();
        Assert.Equal("panel-thin-selected-hover", Source(item, "Sheet"));
        ForceState.SetStates(item, "");
        Screenshots.Settle();
        Assert.Equal("panel-thin-selected", Source(item, "Sheet"));
    }

    [AvaloniaFact]
    public void TileCardRowUsesTheTiledArt()
    {
        var item = new ListBoxItem { Content = "Server", Classes = { "tile" } };
        var list = Host(new ListBox { Classes = { "cards" }, Items = { item }, SelectedIndex = 0 });
        Assert.Equal("panel-tile-selected", Source(item, "Sheet"));
        Assert.True(item.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == "Sheet").TileX);
    }

    [AvaloniaTheory]
    [InlineData("ghost", "", false, "")]
    [InlineData("ghost", ":pointerover", true, "ghost-leather-hover")]
    [InlineData("ghost", ":pointerover :pressed", true, "ghost-leather-pressed")]
    public void LeatherGhostShowsAPlateOnlyOnHoverAndPress(string cls, string states, bool visible, string source)
    {
        var button = Host(new Button { Content = "Label", Classes = { cls } });
        ForceState.SetStates(button, states);
        Screenshots.Settle();
        var plate = button.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == "Plate");
        Assert.Equal(visible, plate.IsVisible);
        if (visible)
        {
            Assert.Equal(source, plate.Source);
        }
    }

    [AvaloniaTheory]
    [InlineData("", "ghost-parchment-normal")]
    [InlineData(":pointerover", "ghost-parchment-hover")]
    [InlineData(":pointerover :pressed", "ghost-parchment-pressed")]
    public void ParchmentGhostAlwaysHasItsOutlinePlate(string states, string expected)
    {
        var button = Host(new Button { Content = "Label", Classes = { "ghost" } });
        ButtonSurface.SetSurface(button, ButtonSurfaceKind.Parchment);
        Screenshots.Settle();
        ForceState.SetStates(button, states);
        Screenshots.Settle();
        Assert.Equal(expected, Source(button, "Plate"));
    }

    [AvaloniaTheory]
    [InlineData("PART_PrimaryButton", ":pointerover", "hover")]
    [InlineData("PART_PrimaryButton", ":pointerover :pressed", "pressed")]
    [InlineData("PART_SecondaryButton", ":pointerover", "hover")]
    public void SplitPlateReadsTheStateOfEachSegment(string part, string states, string expected)
    {
        var split = Host(new SplitButton { Content = "Play", Width = 200 });
        var plate = split.GetVisualDescendants().OfType<SplitPlate>().Single();
        var action = split.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_PrimaryButton");
        var menu = split.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_SecondaryButton");
        Assert.Same(action, plate.ActionSegment);
        Assert.Same(menu, plate.MenuSegment);
        Assert.Equal("primary", plate.Variant);
        Assert.Null(SplitPlate.StateOf(action));

        var target = part == "PART_PrimaryButton" ? action : menu;
        ForceState.SetStates(target, states);
        Assert.Equal(expected, SplitPlate.StateOf(target));
        Assert.Null(SplitPlate.StateOf(part == "PART_PrimaryButton" ? menu : action));
    }

    [AvaloniaFact]
    public void SecondarySplitButtonUsesTheDarkPlate()
    {
        var split = Host(new SplitButton { Content = "Play", Width = 200, Classes = { "secondary" } });
        Assert.Equal("secondary", split.GetVisualDescendants().OfType<SplitPlate>().Single().Variant);
    }

    [AvaloniaFact]
    public void ParchmentSplitButtonUsesParchmentPlateAndFocusRing()
    {
        var split = Host(new SplitButton { Content = "Play", Width = 200 });
        ButtonSurface.SetSurface(split, ButtonSurfaceKind.Parchment);
        Screenshots.Settle();
        Assert.Equal("primary-parchment", split.GetVisualDescendants().OfType<SplitPlate>().Single().Variant);
        Assert.Equal("focus-ring-parchment", Source(split, "Ring"));
    }

    [AvaloniaFact]
    public void SecondaryButtonInsideCardGetsParchmentSurface()
    {
        var button = new Button { Content = "Test", Classes = { "secondary" } };
        var card = Host(new ContentControl { Classes = { "card" }, Content = button, Width = 300, Height = 80 });
        Screenshots.Settle();
        Assert.Equal(ButtonSurfaceKind.Parchment, ButtonSurface.GetSurface(button));
        Assert.Equal("button-secondary-parchment-normal", Source(button, "Plate"));
    }

    [AvaloniaFact]
    public void ParchmentSurfaceInheritsFromParentToDescendantButtons()
    {
        var button = new Button { Content = "Test", Classes = { "secondary" } };
        var card = new ContentControl { Classes = { "card" }, Content = button, Width = 300, Height = 80 };
        ButtonSurface.SetSurface(card, ButtonSurfaceKind.Parchment);
        Host(card);
        Screenshots.Settle();
        Assert.Equal(ButtonSurfaceKind.Parchment, ButtonSurface.GetSurface(button));
        Assert.Contains("surface-parchment", button.Classes);
        Assert.Equal("button-secondary-parchment-normal", Source(button, "Plate"));
    }

    [AvaloniaFact]
    public void ToggleArtFollowsTheControlState()
    {
        var box = Host(new CheckBox { Content = "x", IsChecked = true });
        string Art() => box.GetVisualDescendants().OfType<StateArt>().Single().Source!;
        Assert.Equal("checkbox-leather-checked-normal", Art());
        ForceState.SetStates(box, ":pointerover");
        Screenshots.Settle();
        Assert.Equal("checkbox-leather-checked-hover", Art());
        box.IsChecked = null;
        Screenshots.Settle();
        Assert.Equal("checkbox-leather-indeterminate-hover", Art());
        box.IsEnabled = false;
        Screenshots.Settle();
        Assert.Equal("checkbox-leather-indeterminate-disabled", Art());
    }

    [AvaloniaFact]
    public void ParchmentSwitchUsesParchmentArt()
    {
        var toggle = Host(new ToggleButton { Classes = { "switch", "parchment" }, IsChecked = true });
        Assert.Equal("switch-track-parchment-on-normal", toggle.GetVisualDescendants().OfType<StateArt>().Single().Source);
    }

    [AvaloniaFact]
    public void ProgressStripesRunOnlyWhenSwitching()
    {
        var plain = Host(new ProgressBar { Value = 50, Width = 200 });
        Assert.False(plain.GetVisualDescendants().OfType<StripeTile>().Single().IsVisible);
        var switching = Host(new ProgressBar { Value = 50, Width = 200, Classes = { "switching" } });
        Assert.True(switching.GetVisualDescendants().OfType<StripeTile>().Single().IsVisible);
    }

    [AvaloniaFact]
    public void ZeroProgressHidesTheFill()
    {
        var bar = Host(new ProgressBar { Value = 0, Width = 200 });
        Assert.False(bar.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Indicator").IsVisible);
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
        Assert.Equal("tab-parchment-flat", tab.Source);
        Assert.Equal(0, tab.ShadowMargin);
        Assert.True(second.GetVisualDescendants().OfType<NineSlice>().First(n => n.Name == "Shadow").IsVisible);
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

    [AvaloniaFact]
    public void EveryKitPieceHasA2xFileWithExactlyDoubleThePixels()
    {
        var names = Directory.EnumerateFiles(Repo.Path("assets", "ui"), "*.png", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !n!.EndsWith("@2x"))
            .ToList();
        Assert.True(names.Count > 100);
        foreach (var name in names)
        {
            var (normal, normalFactor) = AssetBitmaps.Frame(name!, 1.0);
            var (retina, retinaFactor) = AssetBitmaps.Frame(name!, 2.0);
            Assert.Equal(1, normalFactor);
            Assert.Equal(2, retinaFactor);
            Assert.Equal(normal.PixelSize.Width * 2, retina.PixelSize.Width);
            Assert.Equal(normal.PixelSize.Height * 2, retina.PixelSize.Height);
        }
    }

    [AvaloniaFact]
    public void EveryPieceNamedInTheThemeIsBundled()
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            "\"((?:button|split|ghost|panel|tab|shadow|focus-ring|input|popup|progress|scrollbar|switch|checkbox|radio)-[a-z0-9-]+)\"");
        var found = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Repo.Path("src", "RealmPortal.App", "Theme"), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(File.ReadAllText(file)))
            {
                if (match.Groups[1].Value != "switch-track")
                {
                    found.Add(match.Groups[1].Value);
                }
            }
        }

        Assert.True(found.Count > 40, $"only {found.Count} piece names found");
        foreach (var name in found)
        {
            AssetBitmaps.Frame(name, 1.0);
        }
    }

    [AvaloniaTheory]
    [InlineData("checkbox", "unchecked,checked,indeterminate")]
    [InlineData("radio", "off,on")]
    [InlineData("switch-track", "off,on")]
    public void EveryStateArtNameResolves(string family, string values)
    {
        foreach (var surface in new[] { "leather", "parchment" })
        {
            foreach (var value in values.Split(','))
            {
                foreach (var state in new[] { "normal", "hover", "disabled" })
                {
                    AssetBitmaps.Frame($"{family}-{surface}-{value}-{state}", 1.0);
                }
            }
        }
    }

    [AvaloniaFact]
    public void HiResArtIsUsedFromScale1_5()
    {
        Assert.Equal(1, AssetBitmaps.Frame("tab-parchment-flat", 1.25).Factor);
        Assert.Equal(2, AssetBitmaps.Frame("tab-parchment-flat", 1.5).Factor);
    }
}
