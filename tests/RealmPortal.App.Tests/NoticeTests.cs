using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Controls;

namespace RealmPortal.App.Tests;

public class NoticeTests
{
    private static T Host<T>(T control) where T : Control
    {
        var window = new Window { Width = 600, Height = 400, Content = control };
        window.Show();
        Screenshots.Settle();
        return control;
    }

    [AvaloniaTheory]
    [InlineData(BannerSeverity.Info, ":info")]
    [InlineData(BannerSeverity.Warning, ":warning")]
    [InlineData(BannerSeverity.Error, ":error")]
    public void BannerSeveritySetsExactlyOnePseudoClass(BannerSeverity severity, string expected)
    {
        var banner = Host(new Banner { Severity = severity, Header = "Title", Content = "Body" });
        foreach (var name in new[] { ":info", ":warning", ":error" })
        {
            Assert.Equal(name == expected, banner.Classes.Contains(name));
        }
    }

    [AvaloniaFact]
    public void BannerSeverityChangesTheGlyph()
    {
        var banner = Host(new Banner { Severity = BannerSeverity.Info, Header = "Title", Content = "Body" });
        var glyph = banner.GetVisualDescendants().OfType<Icon>().First(i => i.Name == "Glyph");
        var info = glyph.Data;
        banner.Severity = BannerSeverity.Error;
        Screenshots.Settle();
        Assert.NotEqual(info, glyph.Data);
    }

    [AvaloniaTheory]
    [InlineData(StepState.Done, ":done")]
    [InlineData(StepState.Current, ":current")]
    [InlineData(StepState.Pending, ":pending")]
    public void StepStateSetsExactlyOnePseudoClass(StepState state, string expected)
    {
        var step = Host(new StepIndicator { Label = "Step", State = state });
        foreach (var name in new[] { ":done", ":current", ":pending" })
        {
            Assert.Equal(name == expected, step.Classes.Contains(name));
        }
    }

    [AvaloniaFact]
    public void StepConnectorCanBeHidden()
    {
        var step = Host(new StepIndicator { Label = "Step", ShowConnector = false });
        var connector = step.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Line>().First();
        Assert.False(connector.IsVisible);
    }

    [AvaloniaFact]
    public void ComboBoxOpensAndShowsItsItems()
    {
        var box = Host(new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 1 });
        Assert.Equal("Two", box.SelectedItem);
        box.IsDropDownOpen = true;
        Screenshots.Settle();
        Assert.True(box.IsDropDownOpen);
    }
}
