using RealmPortal.App.Localization;
using RealmPortal.App.Settings;

namespace RealmPortal.App.Demo;

/// <summary>Simulated settings: kept in memory, nothing is written to disk. Rejects what the real service would (a template without {exe}, an empty backup folder).</summary>
public sealed class DemoSettingsStore : ISettingsStore
{
    private SettingsModel _saved = new(
        Loc.Instance.Current,
        "wine {exe}",
        ["gamemoderun"],
        ["-windowed", "-nosound"],
        new Dictionary<string, string?> { ["WINEPREFIX"] = "/home/demo/.wine", ["DXVK_HUD"] = "fps", ["WINEDEBUG"] = "-all" },
        "/home/demo/.local/share/RealmPortal/backups");

    public SettingsModel Load() => _saved;

    public SettingsError? Save(SettingsModel settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CommandTemplate) && !settings.CommandTemplate.Contains("{exe}", StringComparison.Ordinal))
        {
            return new SettingsError("InvalidLauncherSettings");
        }

        if (string.IsNullOrWhiteSpace(settings.BackupFolder))
        {
            return new SettingsError("PathNotFound", settings.BackupFolder);
        }

        _saved = settings;
        return null;
    }
}
