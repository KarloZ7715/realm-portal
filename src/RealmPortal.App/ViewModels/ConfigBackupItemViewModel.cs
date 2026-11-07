using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Localization;
using RealmPortal.Core.IO;
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

    /// <summary>Filename Core writes for the displaced file (<c>servers.json.before-restore</c>).</summary>
    public string BeforeRestoreFileName => FileLabel + ConfigFileBackup.BeforeRestoreSuffix;

    public string CurrentStateText => Loc.Get(CurrentStateKey(Current));

    public string BackupStateText => Loc.Get(BackupStateKey(Backup));

    public string BackupDateText => BackupModifiedAt is { } at ? FormatBackupDate(at) : string.Empty;

    public string DetailText => BackupModifiedAt is { } at
        ? Loc.Format("settings.config.backup.at", FormatBackupDate(at))
        : Loc.Get("settings.config.backup.none");

    /// <summary>Badge status for the current file: Ready = valid (forest), NoResponse = damaged (wax), Inactive = missing.</summary>
    public ServerStatus CurrentBadgeStatus => ToBadge(Current);

    /// <summary>Badge status for the <c>.bak</c>.</summary>
    public ServerStatus BackupBadgeStatus => ToBadge(Backup);

    /// <summary>Restore is offered only when the main file is damaged and the <c>.bak</c> is valid (#196).</summary>
    public bool CanRestore => Current == ConfigFileState.Damaged && Backup == ConfigFileState.Valid;

    /// <summary>Why Restore is disabled (or that the current file needs no restore). Also used as HelpText.</summary>
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

            return Loc.Get("settings.config.restore.reason.unavailable");
        }
    }

    public bool ShowRestoreDisabledReason => !CanRestore;

    /// <summary>
    /// Accessible name for the row: file, backup date when known, and backup state
    /// (e.g. "servers.json backup, Nov 6, 2026, 10:04, Valid").
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
                    FormatBackupDate(at),
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
        OnPropertyChanged(nameof(BackupDateText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(BeforeRestoreFileName));
        OnPropertyChanged(nameof(RestoreDisabledReason));
        OnPropertyChanged(nameof(ShowRestoreDisabledReason));
        OnPropertyChanged(nameof(StatusAccessibleName));
        RestoreCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync() => await _restore(this).ConfigureAwait(true);

    /// <summary>
    /// Long localized date in the UI language (not the OS culture):
    /// EN <c>Nov 6, 2026, 10:04</c>, ES <c>6 nov 2026, 10:04</c>.
    /// </summary>
    // InvariantGlobalization is on: no CultureInfo month names — keep tables for the UI language.
    private static readonly string[] MonthsEn =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly string[] MonthsEs =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    public static string FormatBackupDate(DateTimeOffset at)
    {
        var local = at.ToLocalTime().DateTime;
        var monthIndex = local.Month - 1;
        var time = $"{local.Hour:D2}:{local.Minute:D2}";
        if (Loc.Instance.Current == Language.Es)
        {
            // e.g. "6 nov 2026, 10:04"
            return $"{local.Day} {MonthsEs[monthIndex]} {local.Year}, {time}";
        }

        // e.g. "Nov 6, 2026, 10:04"
        return $"{MonthsEn[monthIndex]} {local.Day}, {local.Year}, {time}";
    }

    private static string CurrentStateKey(ConfigFileState state) => state switch
    {
        ConfigFileState.Valid => "settings.config.state.current.valid",
        ConfigFileState.Damaged => "settings.config.state.current.damaged",
        _ => "settings.config.state.missing",
    };

    private static string BackupStateKey(ConfigFileState state) => state switch
    {
        ConfigFileState.Valid => "settings.config.state.backup.valid",
        ConfigFileState.Damaged => "settings.config.state.backup.damaged",
        _ => "settings.config.state.missing",
    };

    private static ServerStatus ToBadge(ConfigFileState state) => state switch
    {
        ConfigFileState.Valid => ServerStatus.Ready,
        ConfigFileState.Damaged => ServerStatus.NoResponse,
        _ => ServerStatus.Inactive,
    };
}
