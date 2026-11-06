using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public DateTimeOffset? BackupModifiedAt { get; }

    public string CurrentStateText => Loc.Get(StateKey(Current));

    public string BackupStateText => Loc.Get(StateKey(Backup));

    public string DetailText => BackupModifiedAt is { } at
        ? Loc.Format("settings.config.backup.at", at.ToLocalTime().ToString("g"))
        : Loc.Get("settings.config.backup.none");

    /// <summary>Restore is offered only when the main file is damaged and the <c>.bak</c> is valid (#196).</summary>
    public bool CanRestore => Current == ConfigFileState.Damaged && Backup == ConfigFileState.Valid;

    public string StatusAccessibleName => Loc.Format(
        "settings.config.row.accessible",
        FileLabel,
        CurrentStateText,
        BackupStateText);

    public void RefreshText()
    {
        OnPropertyChanged(nameof(FileLabel));
        OnPropertyChanged(nameof(CurrentStateText));
        OnPropertyChanged(nameof(BackupStateText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(StatusAccessibleName));
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync() => await _restore(this).ConfigureAwait(true);

    private static string StateKey(ConfigFileState state) => state switch
    {
        ConfigFileState.Valid => "settings.config.state.valid",
        ConfigFileState.Damaged => "settings.config.state.damaged",
        _ => "settings.config.state.missing",
    };
}
