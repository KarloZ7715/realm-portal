using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;

namespace RealmPortal.App.Localization;

/// <summary>The two UI languages.</summary>
public enum Language
{
    Es,
    En,
}

/// <summary>
/// UI strings in Spanish and English. The tables are flat JSON files (<c>Strings/es.json</c>, <c>Strings/en.json</c>)
/// with the same keys in both; a test fails when a key is missing in one language or used in code or XAML but not defined.
/// <para>
/// XAML uses <c>{loc:T key}</c> (a one-way binding to the indexer, so changing the language refreshes the screen);
/// code uses <see cref="Get"/> and <see cref="Format"/>. View models that cache formatted text listen to <see cref="Changed"/>.
/// The initial language follows the system UI language (Spanish when it starts with "es", English otherwise).
/// </para>
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private Dictionary<string, string> table;

    private Loc()
    {
        Current = FromCulture(CultureInfo.CurrentUICulture.Name);
        table = Load(Current);
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed and the tables were swapped.</summary>
    public event Action? Changed;

    public Language Current { get; private set; }

    /// <summary>Two-letter code shown in the status bar: <c>ES</c> or <c>EN</c>.</summary>
    public string Code => Current == Language.Es ? "ES" : "EN";

    /// <summary>The text for <paramref name="key"/>; <c>!key!</c> when it is missing (a test makes sure nothing is).</summary>
    public string this[string key] => table.TryGetValue(key, out var text) ? text : $"!{key}!";

    public static string Get(string key) => Instance[key];

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Instance[key], arguments);

    public static Language FromCulture(string? cultureName) =>
        cultureName?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true ? Language.Es : Language.En;

    public void SetLanguage(Language language)
    {
        if (language == Current)
        {
            return;
        }

        Current = language;
        table = Load(language);
        // Avalonia refreshes indexer bindings when the indexer property itself ("Item") is reported as changed.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        Changed?.Invoke();
    }

    public void Toggle() => SetLanguage(Current == Language.Es ? Language.En : Language.Es);

    /// <summary>Reads one language table. Public so tests can compare the two tables.</summary>
    public static Dictionary<string, string> Load(Language language)
    {
        var file = language == Language.Es ? "es" : "en";
        using var stream = AssetLoader.Open(new Uri($"avares://RealmPortal.App/Localization/Strings/{file}.json"));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? throw new InvalidOperationException($"{file}.json is empty");
    }
}
