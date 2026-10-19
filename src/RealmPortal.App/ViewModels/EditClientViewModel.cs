using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core.Service;

namespace RealmPortal.App.ViewModels;

/// <summary>Edits a registered client's display name and installation folder.</summary>
public sealed partial class EditClientViewModel : ViewModelBase
{
    private readonly ClientItemViewModel _client;
    private readonly PortalSession? _session;
    private readonly Func<Task<string?>>? _pickFolder;
    private readonly Action<ClientItemViewModel, ClientEntry?> _save;
    private readonly Action _cancel;
    private ServiceError? _serviceError;

    public EditClientViewModel(
        ClientItemViewModel client,
        PortalSession? session,
        Func<Task<string?>>? pickFolder,
        Action<ClientItemViewModel, ClientEntry?> save,
        Action cancel)
    {
        _client = client;
        _session = session;
        _pickFolder = pickFolder;
        _save = save;
        _cancel = cancel;
        _name = client.Name;
        _folder = client.Path;
    }

    public string ClientTitle => _client.Title;

    public string Version => _client.Version;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _folder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(BrowseCommand))]
    private bool _isSaving;

    public string? Error => _serviceError is null ? null : ErrorText.Body(_serviceError);

    public bool HasError => Error is not null;

    public override string StatusText => IsSaving ? Loc.Get("clients.edit.saving") : Loc.Format("clients.edit.status", ClientTitle);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        _serviceError = null;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        if (_session is null)
        {
            _client.ApplyChanges(string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(), Folder.Trim());
            _save(_client, null);
            return;
        }

        IsSaving = true;
        try
        {
            var request = new UpdateClientRequest(
                _client.Id!,
                string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
                string.Equals(Folder.Trim(), _client.Path, StringComparison.Ordinal) ? null : Folder.Trim());
            var result = await Task.Run(() => _session.Service.UpdateClient(request)).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                _serviceError = result.Error;
                OnPropertyChanged(nameof(Error));
                OnPropertyChanged(nameof(HasError));
                return;
            }
            _save(_client, result.Value);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync()
    {
        if (_pickFolder is null)
        {
            _serviceError = null;
            OnPropertyChanged(nameof(Error));
            OnPropertyChanged(nameof(HasError));
            _browseUnavailable = true;
            OnPropertyChanged(nameof(BrowseUnavailable));
            return;
        }

        var folder = await _pickFolder().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(folder))
            Folder = folder;
    }

    private bool _browseUnavailable;

    public bool BrowseUnavailable => _browseUnavailable;

    [RelayCommand]
    private void Cancel() => _cancel();

    public override void RefreshText()
    {
        OnPropertyChanged(nameof(ClientTitle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Error));
    }

    private bool CanSave() => !IsSaving && !string.IsNullOrWhiteSpace(Folder) && Name.Trim().Length <= 64;

    private bool CanBrowse() => !IsSaving;
}
