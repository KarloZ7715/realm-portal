using RealmPortal.App.Localization;
using RealmPortal.App.Settings;
using RealmPortal.Core.Service;

namespace RealmPortal.App.Portal;

/// <summary>Maps <see cref="ISettingsStore"/> to <see cref="IPortalService"/> plus UI language preferences.</summary>
public sealed class PortalSettingsStore(PortalSession session) : ISettingsStore
{
    public SettingsModel Load()
    {
        var language = session.UiPreferences.ResolveLanguage();
        var launcher = session.Service.GetLauncherSettings();
        var backup = session.Service.GetStorageSettings();
        var status = session.Service.GetStatus();
        var launcherValue = launcher.Succeeded ? launcher.Value! : new LauncherSettings();
        var backupFolder = status.Succeeded && !string.IsNullOrEmpty(status.Value!.BackupFolder)
            ? status.Value.BackupFolder
            : backup.Succeeded
                ? backup.Value!.BackupFolder
                : string.Empty;

        return new SettingsModel(
            language,
            launcherValue.CommandTemplate,
            launcherValue.Prefix ?? [],
            launcherValue.Arguments ?? [],
            launcherValue.Environment ?? new Dictionary<string, string?>(),
            backupFolder);
    }

    public SettingsError? Save(SettingsModel settings)
    {
        session.UiPreferences.SetLanguage(settings.Language);

        var launcher = new LauncherSettings(
            settings.CommandTemplate,
            settings.Arguments,
            settings.Environment,
            settings.Prefix);
        var launcherResult = session.Service.SetLauncherSettings(launcher);
        if (!launcherResult.Succeeded)
        {
            return launcherResult.Error!.Code == ServiceErrorCode.InvalidLauncherSettings
                ? new SettingsError("InvalidLauncherSettings")
                : new SettingsError("PathNotFound", settings.BackupFolder);
        }

        var storageResult = session.Service.SetStorageSettings(new StorageSettings(settings.BackupFolder));
        if (!storageResult.Succeeded)
        {
            return new SettingsError(
                storageResult.Error!.Code switch
                {
                    ServiceErrorCode.InvalidLauncherSettings => "InvalidLauncherSettings",
                    _ => "PathNotFound",
                },
                settings.BackupFolder);
        }

        return null;
    }
}
