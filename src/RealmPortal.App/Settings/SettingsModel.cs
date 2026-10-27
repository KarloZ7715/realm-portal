using RealmPortal.App.Localization;

namespace RealmPortal.App.Settings;

/// <summary>What the Settings screen edits. Launcher fields are those of the core's launch settings (no executable or folder: they come from the client).</summary>
public sealed record SettingsModel(
    Language Language,
    string? CommandTemplate,
    IReadOnlyList<string> Prefix,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string?> Environment,
    string BackupFolder,
    bool ClearCacheOnSwitch = false,
    bool AutoCheckUpdates = true);

/// <summary>Why a save failed: a stable code (the UI localizes it) and the field to mark.</summary>
public sealed record SettingsError(string Code, string? Path = null);

/// <summary>Where the Settings screen reads and writes. The real implementation sits on the application service; <see cref="Demo.DemoSettingsStore"/> simulates one.</summary>
public interface ISettingsStore
{
    SettingsModel Load();

    /// <summary>Validates and saves; null when saved, otherwise nothing was saved.</summary>
    SettingsError? Save(SettingsModel settings);
}
