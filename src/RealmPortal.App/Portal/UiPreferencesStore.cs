using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealmPortal.App.Localization;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;

namespace RealmPortal.App.Portal;

/// <summary>
/// UI-only preferences (language, which clients are "in use"). Not part of <see cref="Core.Service.IPortalService"/>.
/// Stored beside the portal config directory.
/// </summary>
public sealed class UiPreferencesStore(string configDirectory, IAtomicFileWriter? writer = null)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly IAtomicFileWriter _writer = writer ?? new AtomicFileWriter();
    private readonly string _path = Path.Combine(configDirectory, "ui-preferences.json");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new LenientWindowPlacementConverter());
        return options;
    }

    public UiPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UiPreferences();
            }

            using var boundedStream = new MemoryStream(PrivateStorage.ReadBounded(_path, PrivateStorage.MaxJsonBytes));
            using var reader = new StreamReader(boundedStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            var loaded = JsonSerializer.Deserialize<UiPreferences>(json, JsonOptions) ?? new UiPreferences();
            if (loaded.Window is { } window && !WindowPlacementFit.HasUsableSize(window))
            {
                return loaded with { Window = null };
            }

            return loaded;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            // A missing, corrupt, or unreadable preferences file must not stop startup.
            return new UiPreferences();
        }
    }

    private static bool IsUnreadable(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or FormatException
            or StoreException
            or ArgumentException;

    public void Save(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var existed = Directory.Exists(configDirectory);
        var directory = OperatingSystem.IsWindows()
            ? Directory.CreateDirectory(configDirectory)
            : Directory.CreateDirectory(configDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!existed && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preferences, JsonOptions));
        _writer.WriteAllBytes(_path, bytes, new FileReplacementPermissions((int)(UnixFileMode.UserRead | UnixFileMode.UserWrite), null));
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

    /// <summary>Saves the main window's last normal bounds and maximized state; other preferences are kept.</summary>
    public void SetWindowPlacement(WindowPlacement placement) =>
        Save(Load() with { Window = placement });

    public void SetLanguage(Language language) =>
        Save(Load() with { LanguageCode = language == Language.En ? "en" : "es" });
}

public sealed record UiPreferences(
    string? LanguageCode = null,
    IReadOnlyList<string>? ClientsInUse = null,
    bool FirstScanCompleted = false,
    WindowPlacement? Window = null)
{
    public HashSet<string> InUseSet => ClientsInUse?.ToHashSet(StringComparer.Ordinal) ?? [];
}

/// <summary>
/// Deserializes <see cref="WindowPlacement"/> leniently: a malformed <c>Window</c> value becomes null so the rest of
/// the preferences file still loads (language, clients in use, first-scan flag).
/// </summary>
internal sealed class LenientWindowPlacementConverter : JsonConverter<WindowPlacement?>
{
    public override WindowPlacement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Use options without this converter to avoid recursion.
            var plain = new JsonSerializerOptions { WriteIndented = options.WriteIndented };
            var placement = document.RootElement.Deserialize<WindowPlacement>(plain);
            return placement is not null && WindowPlacementFit.HasUsableSize(placement) ? placement : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, WindowPlacement? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var plain = new JsonSerializerOptions { WriteIndented = options.WriteIndented };
        JsonSerializer.Serialize(writer, value, plain);
    }
}
