using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core.Service;

namespace RealmPortal.App.ViewModels;

/// <summary>Backup folder and other storage settings from <see cref="IPortalService"/>.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly PortalSession _session;

    public SettingsViewModel(PortalSession session)
    {
        _session = session;
        LoadFromService();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _backupFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => ErrorMessage is not null;

    public override string StatusText => HasError ? ErrorMessage! : Loc.Get("settings.status");

    public override void RefreshText()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        var folder = BackupFolder.Trim();
        if (folder.Length == 0)
        {
            ErrorMessage = Loc.Get("settings.error.empty");
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        var result = _session.Service.SetStorageSettings(new StorageSettings(folder));
        if (!result.Succeeded)
        {
            ErrorMessage = ServiceErrorText.Body(result.Error!);
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        OnPropertyChanged(nameof(StatusText));
    }

    private void LoadFromService()
    {
        var storage = _session.Service.GetStorageSettings();
        if (storage.Succeeded)
        {
            BackupFolder = storage.Value!.BackupFolder;
        }

        var status = _session.Service.GetStatus();
        if (status.Succeeded && !string.IsNullOrEmpty(status.Value!.BackupFolder))
        {
            BackupFolder = status.Value.BackupFolder;
        }
    }
}
