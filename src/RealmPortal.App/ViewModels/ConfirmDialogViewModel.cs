using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

/// <summary>
/// A modal yes/no question drawn over the window. Texts are string-table keys so the dialog follows a language switch.
/// <see cref="Result"/> completes with true when the user confirms and false on Cancel (also Escape or closing).
/// Cancel is the default and holds the initial focus: a stray Enter never confirms a destructive action.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _titleKey;
    private readonly string _bodyKey;
    private readonly string _confirmKey;
    private readonly string _cancelKey;

    public ConfirmDialogViewModel(string titleKey, string bodyKey, string confirmKey, string cancelKey = "dialog.cancel")
    {
        _titleKey = titleKey;
        _bodyKey = bodyKey;
        _confirmKey = confirmKey;
        _cancelKey = cancelKey;
        Loc.Instance.Changed += Refresh;
        _result.Task.ContinueWith(_ => Loc.Instance.Changed -= Refresh, TaskScheduler.Default);
    }

    public string Title => Loc.Get(_titleKey);

    public string Body => Loc.Get(_bodyKey);

    public string ConfirmText => Loc.Get(_confirmKey);

    public string CancelText => Loc.Get(_cancelKey);

    public Task<bool> Result => _result.Task;

    /// <summary>The "Discard" confirmation for a backup that conflicts with newer client files (decided by the architect).</summary>
    public static ConfirmDialogViewModel DiscardBackup() => new("discard.title", "discard.body", "discard.confirm");

    [RelayCommand]
    private void Confirm() => _result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => _result.TrySetResult(false);

    private void Refresh() => OnPropertyChanged(string.Empty);
}
