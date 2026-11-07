using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Controls;
using RealmPortal.Core.Service;

namespace RealmPortal.App.ViewModels;

public enum RecoveryDestination { Servers, Clients, ScanClients, AddClient, ConfigBackups, LauncherSettings }

/// <summary>A localized next step for an error, with operation-specific retry and guarded navigation.
/// Never launches, closes the game, forces a write, or restores a file on its own.</summary>
public sealed partial class ErrorRecoveryViewModel : ObservableObject
{
    private ServiceError? _error;
    private Func<Task>? _retry;
    private Func<Task>? _fix;
    private Func<Task>? _discard;
    private bool _canRestore;
    private string? _causeKey;
    private bool? _failureOverride;

    public event Func<RecoveryDestination, Task>? NavigateRequested;
    public Func<ServiceError, bool>? CanRestoreConfig { get; set; }
    public ServiceErrorAction Action => _error?.NextAction() ?? ServiceErrorAction.None;
    public bool HasCause => _error is not null;
    public bool IsVisible => HasCause;
    public string Title => Loc.Get("error.banner.title");
    public string Cause => _causeKey is not null ? Loc.Get(_causeKey) : _error is null ? string.Empty : ErrorText.NoticeBody(_error);
    public string AccessibleName => $"{Title}. {Cause}";
    public string PathText => _error?.Path ?? string.Empty;
    public bool HasPath => !string.IsNullOrWhiteSpace(PathText);
    public IReadOnlyList<string> ConflictingFiles => _error?.ConflictingFiles ?? [];
    public bool HasConflictingFiles => ConflictingFiles.Count > 0;
    public bool IsFailure => _failureOverride ?? (_error?.Consistency is Consistency.Inconsistent or Consistency.AppliedLaunchFailed ||
        _error?.Code is ServiceErrorCode.InconsistentState or ServiceErrorCode.LaunchFailed or ServiceErrorCode.InvalidLauncherSettings
            or ServiceErrorCode.PathEscapesClient or ServiceErrorCode.VerifyFailed or ServiceErrorCode.ChecksumMismatch or ServiceErrorCode.Unexpected);
    public BannerSeverity Severity => IsFailure ? BannerSeverity.Error : BannerSeverity.Warning;
    public string Hint => Action != ServiceErrorAction.None ? ErrorText.NextStep(Action) : string.Empty;
    public string PrimaryText => HasPrimary ? Loc.Get($"recovery.{Action.ToString().ToLowerInvariant()}.button") : string.Empty;
    public string SecondaryText => Loc.Get(Action == ServiceErrorAction.ScanOrAddClient ? "recovery.addclient" : "discard.confirm");
    public bool HasPrimary => HasCause && Action switch
    {
        ServiceErrorAction.TryAgain or ServiceErrorAction.CloseGame or ServiceErrorAction.CheckPermissions
            or ServiceErrorAction.FreeDiskSpace or ServiceErrorAction.ResolveUndoConflict => _retry is not null,
        ServiceErrorAction.FixInput => _fix is not null,
        ServiceErrorAction.RestoreConfigBackup => _canRestore,
        ServiceErrorAction.None => false,
        _ => true,
    };
    public bool HasSecondary => IsVisible && (Action == ServiceErrorAction.ScanOrAddClient ||
        Action == ServiceErrorAction.ResolveUndoConflict && _discard is not null);

    public void Show(ServiceError error, Func<Task>? retry = null, Func<Task>? fix = null, Func<Task>? discard = null, string? causeKey = null)
    {
        _causeKey = causeKey;
        _failureOverride = null;
        _error = error;
        _retry = retry;
        _fix = fix;
        _discard = discard;
        RefreshText();
    }

    public void ShowMessage(string causeKey, bool failure = false)
    {
        Show(new ServiceError(ServiceErrorCode.BackupNotFound), causeKey: causeKey);
        _failureOverride = failure;
        RefreshText();
    }

    public void Clear()
    {
        _causeKey = null;
        _failureOverride = null;
        _error = null;
        _retry = _fix = _discard = null;
        RefreshText();
    }

    public void RefreshText()
    {
        _canRestore = _error is { } error && Action == ServiceErrorAction.RestoreConfigBackup && CanRestoreConfig?.Invoke(error) == true;
        OnPropertyChanged(string.Empty);
        PrimaryCommand.NotifyCanExecuteChanged();
        SecondaryCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasPrimary))]
    private Task PrimaryAsync() => Action switch
    {
        ServiceErrorAction.TryAgain or ServiceErrorAction.CloseGame or ServiceErrorAction.CheckPermissions
            or ServiceErrorAction.FreeDiskSpace or ServiceErrorAction.ResolveUndoConflict => _retry?.Invoke() ?? Task.CompletedTask,
        ServiceErrorAction.FixInput => _fix?.Invoke() ?? Task.CompletedTask,
        ServiceErrorAction.ChooseClientFolder => _fix?.Invoke() ?? NavigateAsync(RecoveryDestination.AddClient),
        ServiceErrorAction.ChooseServer => NavigateAsync(RecoveryDestination.Servers),
        ServiceErrorAction.ScanOrAddClient => NavigateAsync(RecoveryDestination.ScanClients),
        ServiceErrorAction.RestoreConfigBackup => NavigateAsync(RecoveryDestination.ConfigBackups),
        ServiceErrorAction.CheckLauncherSettings => NavigateAsync(RecoveryDestination.LauncherSettings),
        _ => NavigateAsync(RecoveryDestination.Clients),
    };

    [RelayCommand(CanExecute = nameof(HasSecondary))]
    private Task SecondaryAsync() => Action == ServiceErrorAction.ScanOrAddClient
        ? NavigateAsync(RecoveryDestination.AddClient) : _discard?.Invoke() ?? Task.CompletedTask;

    private Task NavigateAsync(RecoveryDestination destination) => NavigateRequested?.Invoke(destination) ?? Task.CompletedTask;
}
