using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
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

    public event Func<RecoveryDestination, Task>? NavigateRequested;
    public Func<ServiceError, bool>? CanRestoreConfig { get; set; }
    public ServiceErrorAction Action => _error?.NextAction() ?? ServiceErrorAction.None;
    public bool HasCause => _error is not null;
    public bool IsVisible => _error is not null && Action != ServiceErrorAction.None;
    public string Cause => _error is null ? string.Empty : ErrorText.Body(_error);
    public string Hint => IsVisible ? ErrorText.NextStep(Action) : string.Empty;
    public string PrimaryText => HasPrimary ? Loc.Get($"recovery.{Action.ToString().ToLowerInvariant()}.button") : string.Empty;
    public string SecondaryText => Loc.Get(Action == ServiceErrorAction.ScanOrAddClient ? "recovery.addclient" : "discard.confirm");
    public bool HasPrimary => IsVisible && Action switch
    {
        ServiceErrorAction.TryAgain or ServiceErrorAction.CloseGame or ServiceErrorAction.CheckPermissions
            or ServiceErrorAction.FreeDiskSpace or ServiceErrorAction.ResolveUndoConflict => _retry is not null,
        ServiceErrorAction.FixInput => _fix is not null,
        ServiceErrorAction.RestoreConfigBackup => _canRestore,
        _ => true,
    };
    public bool HasSecondary => IsVisible && (Action == ServiceErrorAction.ScanOrAddClient ||
        Action == ServiceErrorAction.ResolveUndoConflict && _discard is not null);

    public void Show(ServiceError error, Func<Task>? retry = null, Func<Task>? fix = null, Func<Task>? discard = null)
    {
        _error = error;
        _retry = retry;
        _fix = fix;
        _discard = discard;
        RefreshText();
    }

    public void Clear()
    {
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
