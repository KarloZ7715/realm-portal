using RealmPortal.App.Localization;
using RealmPortal.App.Settings;
using RealmPortal.Core.Service;

namespace RealmPortal.App.Portal;

/// <summary>Maps <see cref="ISettingsStore"/> to <see cref="IPortalService"/> plus UI language preferences and network settings.</summary>
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

        var clearCache = backup.Succeeded && backup.Value!.ClearCacheOnSwitch;
        var autoCheck = session.NetworkSettings.LoadOrDefault(out _).UpdatesAutoCheck;
        return new SettingsModel(
            language,
            launcherValue.CommandTemplate,
            launcherValue.Prefix ?? [],
            launcherValue.Arguments ?? [],
            launcherValue.Environment ?? new Dictionary<string, string?>(),
            backupFolder,
            clearCache,
            autoCheck);
    }

    public SettingsError? Save(SettingsModel settings)
    {
        // Network settings first so a failure commits nothing else.
        // Valid file: Update only UpdatesAutoCheck so concurrent LastAttemptUtc / ETag are preserved.
        // Corrupt JSON/empty: Overwrite after moving the bad file aside.
        // Unsupported version / unreadable: preserve the file and fail the save (do not reset toggles).
        var currentNetwork = session.NetworkSettings.LoadOrDefault(out var networkError);
        if (networkError is not null)
        {
            if (!NetworkSettingsStore.IsCorruptContent(networkError))
                return new SettingsError("StoreCorrupt");
            try
            {
                session.NetworkSettings.Overwrite(currentNetwork with { UpdatesAutoCheck = settings.AutoCheckUpdates });
            }
            catch (Exception)
            {
                return new SettingsError("StoreCorrupt");
            }
        }
        else if (currentNetwork.UpdatesAutoCheck != settings.AutoCheckUpdates)
        {
            try
            {
                session.NetworkSettings.Update(s => s with { UpdatesAutoCheck = settings.AutoCheckUpdates });
            }
            catch (Exception)
            {
                return new SettingsError("StoreCorrupt");
            }
        }

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

        var storageResult = session.Service.SetStorageSettings(new StorageSettings(settings.BackupFolder, settings.ClearCacheOnSwitch));
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

        session.UiPreferences.SetLanguage(settings.Language);
        return null;
    }
}
