using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Settings;
using RealmPortal.Core;

namespace RealmPortal.App.ViewModels;

/// <summary>The window: the sidebar sections, the content area and the status bar.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ViewModelBase[] _sections;
    private readonly PortalSession? _session;
    private readonly IConnectionProbe? _probe;
    private readonly Func<Task<string?>>? _pickFolder;
    private AddServerViewModel? _adding;
    private EditClientViewModel? _editingClient;

    public static MainViewModel ForDemo(DemoBackend? backend = null, bool autoTick = true, ISettingsStore? settings = null) =>
        new(backend, autoTick, settings);

    public MainViewModel(DemoBackend? backend = null, bool autoTick = true, ISettingsStore? settings = null)
    {
        Backend = backend ?? new DemoBackend();
        Servers = new ServersViewModel(Backend, autoTick);
        Clients = new ClientsViewModel(Backend);
        var settingsScreen = new SettingsViewModel(
            settings ?? new DemoSettingsStore(),
            () => Task.FromResult<string?>("/home/demo/.local/share/RealmPortal/backups-2"));
        settingsScreen.ApplySavedLanguage();
        settingsScreen.BackRequested += () => SectionIndex = 0;
        Settings = settingsScreen;
        _sections = [Servers, Clients, Settings];
        WireSections();
        Clients.ClientEditRequested += ShowEditClient;
        Clients.ConfirmRemoveAsync = ConfirmRemoveClientAsync;
    }

    public MainViewModel(PortalSession session, Func<Task<string?>>? pickFolder = null, bool autoTick = true, IConnectionProbe? probe = null)
    {
        _session = session;
        _probe = probe ?? new TcpConnectionProbe();
        _pickFolder = pickFolder;
        Servers = new ServersViewModel(session, autoTick);
        Clients = new ClientsViewModel(session, pickFolder, startFirstScan: false);
        var settingsScreen = new SettingsViewModel(new PortalSettingsStore(session), pickFolder);
        settingsScreen.ApplySavedLanguage();
        settingsScreen.BackRequested += () => SectionIndex = 0;
        Settings = settingsScreen;
        _sections = [Servers, Clients, Settings];
        WireSections();
        Clients.ClientsChanged += () => Servers.ReloadFromService();
        Clients.ClientEditRequested += ShowEditClient;
        Clients.ConfirmRemoveAsync = ConfirmRemoveClientAsync;
        Clients.FirstScanFoundNoClients += () => SectionIndex = 1;
        Servers.ConfirmDiscardAsync = () => ConfirmAsync(ConfirmDialogViewModel.DiscardBackup());
        Clients.StartFirstScanIfNeeded();
    }

    public DemoBackend? Backend { get; }

    public ServersViewModel Servers { get; }

    public ClientsViewModel Clients { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Index in the sidebar: 0 servers, 1 clients, 2 settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Content), nameof(StatusText))]
    private int _sectionIndex;

    /// <summary>The form being filled in, or null. It belongs to the servers section: leaving the section drops it.</summary>
    public AddServerViewModel? Adding => _adding;

    public ViewModelBase Content => SectionIndex == 0 && _adding is not null
        ? _adding
        : SectionIndex == 1 && _editingClient is not null
            ? _editingClient
            : _sections[Math.Clamp(SectionIndex, 0, _sections.Length - 1)];

    public string StatusText => Content.StatusText;

    /// <summary>"ES · v0.1.0": the language toggle of the status bar.</summary>
    public string LanguageLabel => $"{Loc.Instance.Code} · v{AppInfo.Version}";

    /// <summary>The non-affiliation note shown at the foot of the wide sidebar.</summary>
    public string Disclaimer => Loc.Instance.Current == Language.Es ? AppInfo.DisclaimerEs : AppInfo.DisclaimerEn;

    /// <summary>The modal question being shown, or null.</summary>
    [ObservableProperty]
    private ConfirmDialogViewModel? _dialog;

    /// <summary>Raised before <see cref="Dialog"/> is set so the window can capture keyboard focus.</summary>
    public event Action? DialogOpening;

    /// <summary>Raised after <see cref="Dialog"/> is cleared so the window can restore keyboard focus.</summary>
    public event Action? DialogClosed;

    /// <summary>Shows <paramref name="dialog"/> over the window and returns the user's answer; a second dialog while one is open answers false.</summary>
    public async Task<bool> ConfirmAsync(ConfirmDialogViewModel dialog)
    {
        if (Dialog is not null)
        {
            dialog.Reject();
            return false;
        }

        DialogOpening?.Invoke();
        Dialog = dialog;
        try
        {
            return await dialog.Result;
        }
        finally
        {
            Dialog = null;
            DialogClosed?.Invoke();
        }
    }

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
        if (value != 1)
            CloseEditClient();
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
            }, CloseAddServer, _probe);
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
        _adding.OnFormClosed();
        _adding = null;
        OnPropertyChanged(nameof(Adding));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private void ShowEditClient(ClientItemViewModel client)
    {
        if (_editingClient is not null || client.IsRunning || client.IsStateUnknown)
            return;

        SectionIndex = 1;
        _editingClient = new EditClientViewModel(client, _session, _pickFolder, (item, updated) =>
        {
            if (updated is not null && item.Id is { } oldId)
                Clients.ApplyClientUpdate(oldId, updated, item.IsInUse);
            CloseEditClient();
        }, CloseEditClient);
        _editingClient.PropertyChanged += OnSectionChanged;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private void CloseEditClient()
    {
        if (_editingClient is null)
            return;

        _editingClient.PropertyChanged -= OnSectionChanged;
        _editingClient = null;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private async Task<bool> ConfirmRemoveClientAsync(ClientItemViewModel client)
    {
        var linkedCount = 0;
        if (_session is not null && client.Id is { } clientId)
        {
            var servers = _session.Service.ListServers(compatibleOnly: false);
            if (!servers.Succeeded)
                return false;
            linkedCount = servers.Value!.Count(server => server.ClientId == clientId);
        }

        var dialog = new ConfirmDialogViewModel(
            "confirm.deleteclient.title",
            linkedCount > 0 ? "confirm.deleteclient.linkedbody" : "confirm.deleteclient.body",
            "confirm.deleteclient.accept",
            bodyArguments: [client.Title, client.Version]);
        return await ConfirmAsync(dialog).ConfigureAwait(true);
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
        _editingClient?.RefreshText();
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(StatusText));
    }
}
