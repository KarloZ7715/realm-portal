using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core;

namespace RealmPortal.App.ViewModels;

/// <summary>The window: the sidebar sections, the content area and the status bar.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ViewModelBase[] _sections;
    private readonly PortalSession? _session;
    private AddServerViewModel? _adding;

    public static MainViewModel ForDemo(DemoBackend? backend = null, bool autoTick = true) =>
        new(backend ?? new DemoBackend(), autoTick);

    private MainViewModel(DemoBackend backend, bool autoTick)
    {
        Backend = backend;
        Servers = new ServersViewModel(backend, autoTick);
        Clients = new ClientsViewModel(backend);
        Settings = new PlaceholderViewModel("nav.settings");
        _sections = [Servers, Clients, Settings];
        WireSections();
    }

    public MainViewModel(PortalSession session, Func<Task<string?>>? pickFolder = null, bool autoTick = true)
    {
        _session = session;
        Loc.Instance.SetLanguage(session.UiPreferences.ResolveLanguage());
        Servers = new ServersViewModel(session, autoTick);
        Clients = new ClientsViewModel(session, pickFolder);
        Settings = new SettingsViewModel(session);
        _sections = [Servers, Clients, Settings];
        WireSections();
        Clients.ClientsChanged += () => Servers.ReloadFromService();
        Servers.ConfirmDiscardAsync = ConfirmDiscardAsync;
    }

    public DemoBackend? Backend { get; }

    public ServersViewModel Servers { get; }

    public ClientsViewModel Clients { get; }

    public ViewModelBase Settings { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Content), nameof(StatusText))]
    private int _sectionIndex;

    public AddServerViewModel? Adding => _adding;

    public ViewModelBase Content => SectionIndex == 0 && _adding is not null
        ? _adding
        : _sections[Math.Clamp(SectionIndex, 0, _sections.Length - 1)];

    public string StatusText => Content.StatusText;

    public string LanguageLabel => $"{Loc.Instance.Code} · v{AppInfo.Version}";

    public string Disclaimer => Loc.Instance.Current == Language.Es ? AppInfo.DisclaimerEs : AppInfo.DisclaimerEn;

    [RelayCommand]
    private void ToggleLanguage()
    {
        Loc.Instance.Toggle();
        _session?.UiPreferences.SetLanguage(Loc.Instance.Current);
    }

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

        if (_session is not null)
        {
            _adding = new AddServerViewModel(Clients, _session, () =>
            {
                Servers.ReloadFromService();
                CloseAddServer();
            }, CloseAddServer);
        }
        else
        {
            _adding = new AddServerViewModel(Clients, Backend!, seed =>
            {
                Servers.AddServer(seed);
                CloseAddServer();
            }, CloseAddServer);
        }

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

    private void WireSections()
    {
        foreach (var section in _sections)
        {
            section.PropertyChanged += OnSectionChanged;
        }

        Servers.AddServerRequested += ShowAddServer;
        Loc.Instance.Changed += OnLanguageChanged;
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

    /// <summary>
    /// Discard confirmation until PR #34 <c>ConfirmDialog</c> is stacked: auto-confirm in tests; production uses discard strings only on the toast action.
    /// </summary>
    private Task<bool> ConfirmDiscardAsync() => Task.FromResult(true);
}
