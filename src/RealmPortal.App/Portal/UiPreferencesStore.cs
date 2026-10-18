using System.Text;
using System.Text.Json;
using RealmPortal.App.Localization;
using RealmPortal.Core.IO;

namespace RealmPortal.App.Portal;

/// <summary>
/// UI-only preferences (language, which clients are "in use"). Not part of <see cref="Core.Service.IPortalService"/>.
/// Stored beside the portal config directory.
/// </summary>
public sealed class UiPreferencesStore(string configDirectory, IAtomicFileWriter? writer = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAtomicFileWriter _writer = writer ?? new AtomicFileWriter();
    private readonly string _path = Path.Combine(configDirectory, "ui-preferences.json");

    public UiPreferences Load()
    {
        if (!File.Exists(_path))
        {
            return new UiPreferences();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UiPreferences>(json, JsonOptions) ?? new UiPreferences();
        }
        catch (JsonException)
        {
            return new UiPreferences();
        }
    }

    public void Save(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(configDirectory);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preferences, JsonOptions));
        _writer.WriteAllBytes(_path, bytes);
    }

    public Language ResolveLanguage()
    {
        var code = Load().LanguageCode;
        if (code is null)
        {
            return Loc.Instance.Current;
        }

        return code.Equals("en", StringComparison.OrdinalIgnoreCase) ? Language.En : Language.Es;
    }

    public void SetLanguage(Language language) =>
        Save(Load() with { LanguageCode = language == Language.En ? "en" : "es" });
}

public sealed record UiPreferences(string? LanguageCode = null, IReadOnlyList<string>? ClientsInUse = null)
{
    public HashSet<string> InUseSet => ClientsInUse?.ToHashSet(StringComparer.Ordinal) ?? [];
}
