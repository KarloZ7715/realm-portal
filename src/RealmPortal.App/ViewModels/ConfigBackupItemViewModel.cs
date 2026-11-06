using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Localization;
using RealmPortal.Core.Service;

namespace RealmPortal.App.ViewModels;

/// <summary>One app config file and its <c>.bak</c> on the Settings screen (#196 UI).</summary>
public sealed partial class ConfigBackupItemViewModel : ObservableObject
{
    private readonly Func<ConfigBackupItemViewModel, Task> _restore;

    public ConfigBackupItemViewModel(
        string fileKey,
        string path,
        ConfigFileState current,
        ConfigFileState backup,
        DateTimeOffset? backupModifiedAt,
        Func<ConfigBackupItemViewModel, Task> restore,
        ConfigFileKind? kind = null,
        bool isPreferences = false)
    {
        FileKey = fileKey;
        Path = path;
        Current = current;
        Backup = backup;
        BackupModifiedAt = backupModifiedAt;
        Kind = kind;
        IsPreferences = isPreferences;
        _restore = restore;
    }

    public string FileKey { get; }

    public string FileLabel => Loc.Get(FileKey);

    public string Path { get; }

    public ConfigFileKind? Kind { get; }

    public bool IsPreferences { get; }

    public ConfigFileState Current { get; }

    public ConfigFileState Backup { get; }

    /// <summary>Last-write time of the <c>.bak</c> when Core exposes it (UTC from the service).</summary>
    public DateTimeOffset? BackupModifiedAt { get; }

    public string CurrentStateText => Loc.Get(StateKey(Current));

    public string BackupStateText => Loc.Get(StateKey(Backup));

    public string DetailText => BackupModifiedAt is { } at
        ? Loc.Format("settings.config.backup.at", at.ToLocalTime().ToString("g"))
        : Loc.Get("settings.config.backup.none");

    /// <summary>Badge status for the current file: Ready = valid (forest), NoResponse = damaged (wax), Inactive = missing.</summary>
    public ServerStatus CurrentBadgeStatus => ToBadge(Current);

    /// <summary>Badge status for the <c>.bak</c>.</summary>
    public ServerStatus BackupBadgeStatus => ToBadge(Backup);

    /// <summary>Restore is offered only when the main file is damaged and the <c>.bak</c> is valid (#196).</summary>
    public bool CanRestore => Current == ConfigFileState.Damaged && Backup == ConfigFileState.Valid;

    /// <summary>Why Restore is disabled (or that the current file needs no restore).</summary>
    public string RestoreDisabledReason
    {
        get
        {
            if (CanRestore)
            {
                return string.Empty;
            }

            if (Current == ConfigFileState.Valid)
            {
                return Loc.Get("settings.config.restore.reason.ok");
            }

            if (Backup == ConfigFileState.Damaged)
            {
                return Loc.Get("settings.config.restore.reason.damaged");
            }

            if (Backup == ConfigFileState.Missing)
            {
                return Loc.Get("settings.config.restore.reason.missing");
            }

            // Current damaged but backup not valid — cover any other combo.
            return Loc.Get("settings.config.restore.reason.unavailable");
        }
    }

    public bool ShowRestoreDisabledReason => !CanRestore;

    /// <summary>
    /// Accessible name for the row: file, backup date when known, and backup state
    /// (e.g. "servers.json backup, 5 Nov 2026, 21:40, Valid").
    /// </summary>
    public string StatusAccessibleName
    {
        get
        {
            if (BackupModifiedAt is { } at)
            {
                return Loc.Format(
                    "settings.config.row.accessible",
                    FileLabel,
                    at.ToLocalTime().ToString("g"),
                    BackupStateText);
            }

            return Loc.Format("settings.config.row.accessible.nodate", FileLabel, BackupStateText);
        }
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(FileLabel));
        OnPropertyChanged(nameof(CurrentStateText));
        OnPropertyChanged(nameof(BackupStateText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(RestoreDisabledReason));
        OnPropertyChanged(nameof(ShowRestoreDisabledReason));
        OnPropertyChanged(nameof(StatusAccessibleName));
        RestoreCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync() => await _restore(this).ConfigureAwait(true);

    private static string StateKey(ConfigFileState state) => state switch
    {
        ConfigFileState.Valid => "settings.config.state.valid",
        ConfigFileState.Damaged => "settings.config.state.damaged",
        _ => "settings.config.state.missing",
    };

    private static ServerStatus ToBadge(ConfigFileState state) => state switch
    {
        ConfigFileState.Valid => ServerStatus.Ready,
        ConfigFileState.Damaged => ServerStatus.NoResponse,
        _ => ServerStatus.Inactive,
    };
}
