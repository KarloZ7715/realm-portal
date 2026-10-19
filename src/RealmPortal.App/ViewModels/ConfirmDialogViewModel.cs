using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

public enum ConfirmDialogChoice { Cancel, Confirm, Secondary }

/// <summary>
/// A modal yes/no question drawn over the window. Texts are string-table keys so the dialog follows a language switch.
/// <see cref="Result"/> completes with true when the user confirms and false on Cancel (also Escape or closing).
/// Cancel is the default and holds the initial focus: a stray Enter never confirms a destructive action.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<ConfirmDialogChoice> _choice = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _titleKey;
    private readonly string _bodyKey;
    private readonly string _confirmKey;
    private readonly string _cancelKey;
    private readonly string? _secondaryActionKey;
    private readonly object?[] _bodyArguments;

    public ConfirmDialogViewModel(
        string titleKey,
        string bodyKey,
        string confirmKey,
        string cancelKey = "dialog.cancel",
        object?[]? bodyArguments = null,
        string? secondaryActionKey = null)
    {
        _titleKey = titleKey;
        _bodyKey = bodyKey;
        _confirmKey = confirmKey;
        _cancelKey = cancelKey;
        _secondaryActionKey = secondaryActionKey;
        _bodyArguments = bodyArguments ?? [];
        Loc.Instance.Changed += Refresh;
        _choice.Task.ContinueWith(_ => Loc.Instance.Changed -= Refresh, TaskScheduler.Default);
    }

    public string Title => Loc.Get(_titleKey);

    public string Body => _bodyArguments.Length == 0 ? Loc.Get(_bodyKey) : Loc.Format(_bodyKey, _bodyArguments);

    public string ConfirmText => Loc.Get(_confirmKey);

    public string CancelText => Loc.Get(_cancelKey);

    public string SecondaryActionText => _secondaryActionKey is null ? string.Empty : Loc.Get(_secondaryActionKey);

    public bool HasSecondaryAction => _secondaryActionKey is not null;

    public Task<ConfirmDialogChoice> Choice => _choice.Task;

    public Task<bool> Result => _result.Task;

    /// <summary>The "Discard" confirmation for a backup that conflicts with newer client files (decided by the architect).</summary>
    public static ConfirmDialogViewModel DiscardBackup() => new("discard.title", "discard.body", "discard.confirm");

    public static ConfirmDialogViewModel BackupConflict(string serverName, string switchedAt, string backupLabel) =>
        new("confirm.backupconflict.title", "confirm.backupconflict.body", "confirm.backupconflict.restoreanyway",
            "confirm.cancel", [serverName, switchedAt, backupLabel], "confirm.backupconflict.discard");

    [RelayCommand]
    private void Confirm() => Complete(ConfirmDialogChoice.Confirm);

    [RelayCommand]
    private void Cancel() => Complete(ConfirmDialogChoice.Cancel);

    [RelayCommand]
    private void SecondaryAction() => Complete(ConfirmDialogChoice.Secondary);

    /// <summary>Completes with false and unsubscribes when the dialog cannot be shown (e.g. another is already open).</summary>
    public void Reject()
    {
        if (_choice.Task.IsCompleted)
        {
            return;
        }

        Loc.Instance.Changed -= Refresh;
        Complete(ConfirmDialogChoice.Cancel);
    }

    private void Complete(ConfirmDialogChoice choice)
    {
        _choice.TrySetResult(choice);
        _result.TrySetResult(choice == ConfirmDialogChoice.Confirm);
    }

    private void Refresh() => OnPropertyChanged(string.Empty);
}
