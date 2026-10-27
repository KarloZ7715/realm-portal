using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;

namespace RealmPortal.App.ViewModels;

/// <summary>
/// Modal update notice (#105): version, plain release notes (≤20k, no HTML/links), Update / Later / Skip.
/// "Open on GitHub" uses <see cref="UpdateOffer.HtmlUrl"/> when present.
/// </summary>
public sealed partial class UpdateNoticeViewModel : ObservableObject
{
    private readonly TaskCompletionSource<ConfirmDialogChoice> _choice =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly UpdateOffer _offer;

    public UpdateNoticeViewModel(UpdateOffer offer)
    {
        _offer = offer ?? throw new ArgumentNullException(nameof(offer));
        Loc.Instance.Changed += Refresh;
        _choice.Task.ContinueWith(_ => Loc.Instance.Changed -= Refresh, TaskScheduler.Default);
    }

    public UpdateOffer Offer => _offer;

    public string Title => Loc.Format("update.title", _offer.Version);

    public string Body => Loc.Format("update.body", _offer.Version);

    public string ReleaseNotes => UpdateOffer.ClampNotes(_offer.ReleaseNotesPlain);

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    public string UpdateText => Loc.Get("update.action.update");

    public string LaterText => Loc.Get("update.action.later");

    public string SkipText => Loc.Get("update.action.skip");

    public string OpenGitHubText => Loc.Get("update.action.github");

    public bool CanOpenGitHub => !string.IsNullOrWhiteSpace(_offer.HtmlUrl);

    public Task<ConfirmDialogChoice> Choice => _choice.Task;

    public event Action<string>? OpenUrlRequested;

    [RelayCommand]
    private void Update() => Complete(ConfirmDialogChoice.Confirm);

    [RelayCommand]
    private void Later() => Complete(ConfirmDialogChoice.Secondary);

    [RelayCommand]
    private void Skip() => Complete(ConfirmDialogChoice.Cancel);

    [RelayCommand(CanExecute = nameof(CanOpenGitHub))]
    private void OpenGitHub()
    {
        if (_offer.HtmlUrl is { Length: > 0 } url)
            OpenUrlRequested?.Invoke(url);
    }

    public void Reject()
    {
        if (_choice.Task.IsCompleted)
            return;
        Loc.Instance.Changed -= Refresh;
        // Host dismiss → Later (postpone), not a permanent skip.
        Complete(ConfirmDialogChoice.Secondary);
    }

    private void Complete(ConfirmDialogChoice choice) => _choice.TrySetResult(choice);

    private void Refresh() => OnPropertyChanged(string.Empty);
}
