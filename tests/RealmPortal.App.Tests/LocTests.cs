using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;

namespace RealmPortal.App.Tests;

public partial class LocTests
{
    [AvaloniaFact]
    public void BothLanguagesDefineTheSameKeys()
    {
        var es = Loc.Load(Language.Es).Keys.ToHashSet();
        var en = Loc.Load(Language.En).Keys.ToHashSet();
        Assert.Empty(es.Except(en));
        Assert.Empty(en.Except(es));
    }

    [AvaloniaFact]
    public void NoTranslationIsEmpty()
    {
        foreach (var language in new[] { Language.Es, Language.En })
        {
            Assert.All(Loc.Load(language), pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{language}:{pair.Key}"));
        }
    }

    [AvaloniaFact]
    public void EveryKeyUsedInXamlOrCodeIsDefined()
    {
        var defined = Loc.Load(Language.En).Keys.ToHashSet();
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Repo.Path("src", "RealmPortal.App"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".axaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = string.Join("\n", File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("///")));
            foreach (Match m in XamlKey().Matches(text)) { used.Add(m.Groups[1].Value); }
            foreach (Match m in CodeKey().Matches(text)) { used.Add(m.Groups[1].Value); }
        }

        Assert.Empty(used.Except(defined));
    }

    [AvaloniaFact]
    public void BackLabelsDoNotEmbedArrows()
    {
        // Back buttons draw the IconChevronLeft icon; an arrow in the text would show twice (#97).
        foreach (var language in new[] { Language.Es, Language.En })
        {
            Assert.All(Loc.Load(language).Where(p => p.Key.EndsWith(".back", StringComparison.Ordinal)),
                pair => Assert.DoesNotContain("←", pair.Value));
        }
    }

    [AvaloniaFact]
    public void BackLabelsAreTheSameOnEveryScreen()
    {
        // Welcome, About, Backups (Settings) and Edit client share one Back label per language.
        foreach (var (language, expected) in new[] { (Language.En, "Back"), (Language.Es, "Atrás") })
        {
            var backs = Loc.Load(language).Where(p => p.Key.EndsWith(".back", StringComparison.Ordinal)).ToList();
            Assert.Contains(backs, p => p.Key == "welcome.back");
            Assert.Contains(backs, p => p.Key == "about.back");
            Assert.Contains(backs, p => p.Key == "backups.back");
            Assert.Contains(backs, p => p.Key == "clients.edit.back");
            Assert.All(backs, pair => Assert.Equal(expected, pair.Value));
        }
    }

    [AvaloniaFact]
    public void MissingKeyShowsAMarker()
    {
        Assert.Equal("!missing.key!", Loc.Get("missing.key"));
    }

    [AvaloniaFact]
    public void ExtensionBindingFollowsTheLanguage()
    {
        var loc = Loc.Instance;
        var original = loc.Current;
        try
        {
            loc.SetLanguage(Language.Es);
            var block = new TextBlock();
            block.Bind(TextBlock.TextProperty, new Binding("[language.name]") { Source = loc, Mode = BindingMode.OneWay });
            Assert.Equal("Español", block.Text);
            loc.SetLanguage(Language.En);
            Assert.Equal("English", block.Text);
            Assert.Equal("EN", loc.Code);
            loc.Toggle();
            Assert.Equal("Español", block.Text);
            Assert.Equal("ES", loc.Code);
        }
        finally
        {
            loc.SetLanguage(original);
        }
    }

    [AvaloniaFact]
    public void MarkupExtensionBindsToTheIndexer()
    {
        var binding = Assert.IsType<Binding>(new TExtension("language.name").ProvideValue(null!));
        Assert.Equal("[language.name]", binding.Path);
        Assert.Same(Loc.Instance, binding.Source);
    }

    [Theory]
    [InlineData("es-CO", Language.Es)]
    [InlineData("es", Language.Es)]
    [InlineData("en-US", Language.En)]
    [InlineData("fr-FR", Language.En)]
    [InlineData("", Language.En)]
    public void InitialLanguageFollowsTheCulture(string culture, Language expected) =>
        Assert.Equal(expected, Loc.FromCulture(culture));

    [GeneratedRegex(@"\{loc:T\s+([A-Za-z0-9_.\-]+)\s*\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.(?:Get|Format)\(\s*""([^""]+)""")]
    private static partial Regex CodeKey();
}
