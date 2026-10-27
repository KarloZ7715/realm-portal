using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Platform;
using RealmPortal.Core;

namespace RealmPortal.App.ViewModels;

/// <summary>About sub-sheet: app identity, credits, MIT note, third-party licenses, and the GitHub link.</summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly IUriOpener _uriOpener;
    private readonly Action _back;

    public AboutViewModel(IUriOpener uriOpener, Action backRequested, Func<Stream>? openThirdPartyLicenses = null)
    {
        _uriOpener = uriOpener;
        _back = backRequested;
        AppName = AppInfo.Name;
        Version = AppInfo.Version;
        Copyright = AppInfo.Copyright;
        RepositoryUrl = AppInfo.RepositoryUrl;
        ThirdPartyLicenses = AboutDocuments.LoadThirdPartyLicenses(openThirdPartyLicenses);
        RefreshText();
    }

    public string AppName { get; }

    public string Version { get; }

    public string Copyright { get; }

    public string RepositoryUrl { get; }

    public string ThirdPartyLicenses { get; }

    public bool HasThirdPartyLicenses => !string.IsNullOrWhiteSpace(ThirdPartyLicenses);

    public string VersionLabel => Loc.Format("about.version", Version);

    public string CreditsText => Loc.Get("about.credits");

    public string MitNote => Loc.Get("about.mit");

    public string Disclaimer => Loc.Instance.Current == Language.Es ? AppInfo.DisclaimerEs : AppInfo.DisclaimerEn;

    [ObservableProperty]
    private string? _openError;

    public override string StatusText => Loc.Get("about.status");

    public override void RefreshText()
    {
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(CreditsText));
        OnPropertyChanged(nameof(MitNote));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(StatusText));
        OpenError = null;
    }

    [RelayCommand]
    private void Back() => _back();

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        OpenError = null;
        if (!Uri.TryCreate(RepositoryUrl, UriKind.Absolute, out var uri))
        {
            OpenError = Loc.Get("about.open.failed");
            return;
        }

        var ok = await _uriOpener.OpenAsync(uri).ConfigureAwait(true);
        if (!ok)
        {
            OpenError = Loc.Get("about.open.failed");
        }
    }
}
