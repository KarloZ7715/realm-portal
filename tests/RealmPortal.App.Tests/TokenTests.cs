using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace RealmPortal.App.Tests;

public class TokenTests
{
    [AvaloniaTheory]
    [InlineData("Leather900", "#1A110C")]
    [InlineData("Bone", "#EFE5CD")]
    [InlineData("ParchmentPaper", "#D8C7A0")]
    [InlineData("Ink", "#2B2118")]
    [InlineData("Wax", "#7A2723")]
    [InlineData("FocusLeather", "#F0BC82")]
    [InlineData("FocusParchment", "#0F5561")]
    [InlineData("BronzeDark", "#5A3D22")]
    [InlineData("Stitch", "#8A6C4A")]
    public void ColorTokensMatchTheDesignFile(string name, string hex)
    {
        var color = Res.Get<Color>(name + "Color");
        Assert.Equal(Color.Parse(hex), color);
        var brush = Res.Get<SolidColorBrush>(name + "Brush");
        Assert.Equal(color, brush.Color);
    }

    [AvaloniaFact]
    public void ThirtyFourColorTokensAreDefined()
    {
        var tokens = Application.Current!.Resources.MergedDictionaries
            .OfType<IResourceDictionary>()
            .SelectMany(dictionary => dictionary.Keys)
            .Count(key => key is string name && name.EndsWith("Color", StringComparison.Ordinal));
        Assert.Equal(34, tokens);
    }
}
