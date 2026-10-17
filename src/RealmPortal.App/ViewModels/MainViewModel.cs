using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.Core;

namespace RealmPortal.App.ViewModels;

/// <summary>The window: the sidebar sections, the content area and the status bar.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ViewModelBase[] _sections;
    private AddServerViewModel? _adding;

    public MainViewModel(DemoBackend? backend = null, bool autoTick = true)
    {
        Backend = backend ?? new DemoBackend();
        Servers = new ServersViewModel(Backend, autoTick: autoTick);
        Clients = new ClientsViewModel(Backend);
        Settings = new PlaceholderViewModel("nav.settings");
        _sections = [Servers, Clients, Settings];
        foreach (var section in _sections)
        {
            section.PropertyChanged += OnSectionChanged;
        }

        Servers.AddServerRequested += ShowAddServer;
        Loc.Instance.Changed += OnLanguageChanged;
    }

    public DemoBackend Backend { get; }

    public ServersViewModel Servers { get; }

    public ClientsViewModel Clients { get; }

    public ViewModelBase Settings { get; }

    /// <summary>Index in the sidebar: 0 servers, 1 clients, 2 settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Content), nameof(StatusText))]
    private int _sectionIndex;

    /// <summary>The form being filled in, or null. It belongs to the servers section: leaving the section drops it.</summary>
    public AddServerViewModel? Adding => _adding;

    public ViewModelBase Content => SectionIndex == 0 && _adding is not null
        ? _adding
        : _sections[Math.Clamp(SectionIndex, 0, _sections.Length - 1)];

    public string StatusText => Content.StatusText;

    /// <summary>"ES · v0.1.0": the language toggle of the status bar.</summary>
    public string LanguageLabel => $"{Loc.Instance.Code} · v{AppInfo.Version}";

    /// <summary>The non-affiliation note shown at the foot of the wide sidebar.</summary>
    public string Disclaimer => Loc.Instance.Current == Language.Es ? AppInfo.DisclaimerEs : AppInfo.DisclaimerEn;

    [RelayCommand]
    private void ToggleLanguage() => Loc.Instance.Toggle();

    partial void OnSectionIndexChanged(int value)
    {
        if (value != 0)
        {
            CloseAddServer();
        }
    }

    private void ShowAddServer()
    {
        SectionIndex = 0;
        if (_adding is not null)
        {
            return;
        }

        _adding = new AddServerViewModel(Clients, Backend, seed =>
        {
            Servers.AddServer(seed);
            CloseAddServer();
        }, CloseAddServer);
        _adding.PropertyChanged += OnSectionChanged;
        OnPropertyChanged(nameof(Adding));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private void CloseAddServer()
    {
        if (_adding is null)
        {
            return;
        }

        _adding.PropertyChanged -= OnSectionChanged;
        _adding = null;
        OnPropertyChanged(nameof(Adding));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModelBase.StatusText) && ReferenceEquals(sender, Content))
        {
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void OnLanguageChanged()
    {
        foreach (var section in _sections)
        {
            section.RefreshText();
        }

        _adding?.RefreshText();
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(StatusText));
    }
}
