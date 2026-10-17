using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace RealmPortal.App.Tests;

public class FontTests
{
    private static FontFamily Resource(string key) =>
        Res.Get<FontFamily>(key);

    private static IGlyphTypeface Resolve(string key, FontWeight weight = FontWeight.Normal)
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(
            new Typeface(Resource(key), FontStyle.Normal, weight), out var glyphTypeface),
            $"font resource {key} did not resolve");
        return glyphTypeface;
    }

    [AvaloniaTheory]
    [InlineData("FontDisplay", "Almendra", FontWeight.Bold)]
    [InlineData("FontCaps", "Marcellus SC", FontWeight.Normal)]
    [InlineData("FontBody", "Alegreya", FontWeight.Normal)]
    [InlineData("FontBodyMedium", "Alegreya Medium", FontWeight.Normal)]
    [InlineData("FontSans", "Alegreya Sans", FontWeight.Normal)]
    [InlineData("FontSansMedium", "Alegreya Sans Medium", FontWeight.Normal)]
    [InlineData("FontMono", "JetBrains Mono", FontWeight.Normal)]
    public void BundledFamilyResolvesToItsOwnFile(string key, string expectedFamily, FontWeight weight)
    {
        var glyphTypeface = Resolve(key, weight);
        Assert.Equal(expectedFamily, glyphTypeface.FamilyName);
    }

    [AvaloniaFact]
    public void BoldBodyIsARealBoldFace()
    {
        var bold = Resolve("FontBody", FontWeight.Bold);
        Assert.Equal("Alegreya", bold.FamilyName);
        Assert.True(bold.Weight >= FontWeight.SemiBold);
    }

    [AvaloniaFact]
    public void MonoHasNoCheckMarkGlyphSoTheUiUsesAnIcon()
    {
        var mono = Resolve("FontMono");
        var hasGlyph = mono.TryGetGlyph(0x2713, out var glyph) && glyph != 0;
        Assert.False(hasGlyph);
    }

    [AvaloniaTheory]
    [InlineData("FontDisplay", FontWeight.Bold)]
    [InlineData("FontCaps", FontWeight.Normal)]
    [InlineData("FontBody", FontWeight.Normal)]
    [InlineData("FontBodyMedium", FontWeight.Normal)]
    [InlineData("FontSans", FontWeight.Normal)]
    [InlineData("FontSansMedium", FontWeight.Normal)]
    [InlineData("FontMono", FontWeight.Normal)]
    public void SpanishGlyphsArePresent(string key, FontWeight weight)
    {
        var glyphTypeface = Resolve(key, weight);
        foreach (var ch in "áéíóúñ¿¡üÁÉÍÓÚÑÜ«»—·")
        {
            Assert.True(glyphTypeface.TryGetGlyph(ch, out var glyph) && glyph != 0, $"{key} lacks U+{(int)ch:X4}");
        }
    }
}
