using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Platform;
using RealmPortal.App.Settings;
using RealmPortal.Core;
using RealmPortal.Core.ClientFiles;

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
    private BackupsViewModel? _backups;
    private AboutViewModel? _about;
    private readonly IUriOpener _uriOpener;

    public static MainViewModel ForDemo(DemoBackend? backend = null, bool autoTick = true, ISettingsStore? settings = null, IUriOpener? uriOpener = null) =>
        new(backend, autoTick, settings, uriOpener);

    public MainViewModel(DemoBackend? backend = null, bool autoTick = true, ISettingsStore? settings = null, IUriOpener? uriOpener = null)
    {
        _uriOpener = uriOpener ?? new ShellUriOpener();
        Backend = backend ?? new DemoBackend();
        Servers = new ServersViewModel(Backend, autoTick);
        Clients = new ClientsViewModel(Backend);
        var settingsScreen = new SettingsViewModel(
            settings ?? new DemoSettingsStore(),
            () => Task.FromResult<string?>("/home/demo/.local/share/RealmPortal/backups-2"));
        settingsScreen.ApplySavedLanguage();
        settingsScreen.CanManageBackups = false;
        settingsScreen.OpenBackupsManagerRequested += ShowBackupsManager;
        settingsScreen.OpenAboutRequested += ShowAbout;
        Settings = settingsScreen;
        _sections = [Servers, Clients, Settings];
        WireSections();
        Clients.ClientEditRequested += ShowEditClient;
        Clients.ConfirmRemoveAsync = ConfirmRemoveClientAsync;
    }

    public MainViewModel(PortalSession session, Func<Task<string?>>? pickFolder = null, bool autoTick = true, IConnectionProbe? probe = null, Func<TimeSpan, Task>? delay = null, IUriOpener? uriOpener = null)
    {
        _uriOpener = uriOpener ?? new ShellUriOpener();
        _session = session;
        _probe = probe ?? new TcpConnectionProbe();
        _pickFolder = pickFolder;
        Servers = new ServersViewModel(session, autoTick, delay);
        Clients = new ClientsViewModel(session, pickFolder, startFirstScan: false);
        var settingsScreen = new SettingsViewModel(new PortalSettingsStore(session), pickFolder);
        settingsScreen.ApplySavedLanguage();
        settingsScreen.CanManageBackups = true;
        settingsScreen.OpenBackupsManagerRequested += ShowBackupsManager;
        settingsScreen.OpenAboutRequested += ShowAbout;
        Settings = settingsScreen;
        _sections = [Servers, Clients, Settings];
        WireSections();
        Clients.ClientsChanged += () => Servers.ReloadFromService();
        Clients.ClientEditRequested += ShowEditClient;
        Clients.ConfirmRemoveAsync = ConfirmRemoveClientAsync;
        Clients.FirstScanFoundNoClients += ShowClientsAfterEmptyScan;
        Servers.ConfirmDiscardAsync = () => ConfirmAsync(ConfirmDialogViewModel.DiscardBackup());
        Clients.StartFirstScanIfNeeded();
    }

    public DemoBackend? Backend { get; }

    public ServersViewModel Servers { get; }

    public ClientsViewModel Clients { get; }

    public SettingsViewModel Settings { get; }

    private int _sectionIndex;

    /// <summary>
    /// Index in the sidebar: 0 servers, 1 clients, 2 settings. Leaving Settings with unsaved changes asks first
    /// (Cancel by default); until the user confirms, the sidebar stays on Settings and nothing is discarded.
    /// </summary>
    public int SectionIndex
    {
        get => _sectionIndex;
        set
        {
            if (_sectionIndex == value)
            {
                return;
            }

            if (_sectionIndex == 2 && Settings is { IsDirty: true })
            {
                // The sidebar has already moved its selection; the window puts it back while the question is open.
                SectionChangeHeld?.Invoke();
                _ = ConfirmLeaveSettingsAsync(value);
                return;
            }

            SetSection(value);
        }
    }

    /// <summary>
    /// A scan that found no clients opens the Clients screen, unless the user is editing Settings: a background scan
    /// must not interrupt them with the unsaved-changes question (only navigation and closing ask).
    /// </summary>
    private void ShowClientsAfterEmptyScan()
    {
        if (_sectionIndex == 2 && Settings.IsDirty)
        {
            return;
        }

        SectionIndex = 1;
    }

    /// <summary>Raised when a section change waits for the unsaved-changes question, so the sidebar can re-select Settings.</summary>
    public event Action? SectionChangeHeld;

    private void SetSection(int value)
    {
        if (!SetProperty(ref _sectionIndex, value, nameof(SectionIndex)))
        {
            return;
        }

        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
        OnSectionIndexChanged(value);
    }

    private async Task ConfirmLeaveSettingsAsync(int target)
    {
        if (await ConfirmDiscardSettingsAsync().ConfigureAwait(true))
        {
            SetSection(target);
        }
    }

    /// <summary>
    /// True when nothing in Settings is unsaved, or when the user chose "Discard changes" (the edits are then reverted).
    /// "Keep editing" (the default) answers false and keeps every edit.
    /// </summary>
    public async Task<bool> ConfirmDiscardSettingsAsync()
    {
        if (!Settings.IsDirty)
        {
            return true;
        }

        if (!await ConfirmAsync(ConfirmDialogViewModel.DiscardSettingsChanges()).ConfigureAwait(true))
        {
            return false;
        }

        Settings.CancelCommand.Execute(null);
        // The status-bar language toggle writes the preference right away; discarding must put the saved one back.
        _session?.UiPreferences.SetLanguage(Loc.Instance.Current);
        return true;
    }

    /// <summary>The form being filled in, or null. It belongs to the servers section: leaving the section drops it.</summary>
    public AddServerViewModel? Adding => _adding;

    public ViewModelBase Content => SectionIndex == 2 && _about is not null
        ? _about
        : SectionIndex == 2 && _backups is not null
        ? _backups
        : SectionIndex == 0 && _adding is not null
        ? _adding
        : SectionIndex == 1 && _editingClient is not null
            ? _editingClient
            : _sections[Math.Clamp(SectionIndex, 0, _sections.Length - 1)];

    public string StatusText => Content.StatusText;

    /// <summary>"ES · v1.0.0": the language toggle of the status bar (version from <see cref="AppInfo.Version"/>).</summary>
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
        return await ChooseAsync(dialog).ConfigureAwait(true) == ConfirmDialogChoice.Confirm;
    }

    public async Task<ConfirmDialogChoice> ChooseAsync(ConfirmDialogViewModel dialog)
    {
        if (Dialog is not null)
        {
            dialog.Reject();
            return ConfirmDialogChoice.Cancel;
        }

        DialogOpening?.Invoke();
        Dialog = dialog;
        try
        {
            return await dialog.Choice;
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
        Settings.AcceptSavedLanguage(Loc.Instance.Current);
    }

    private void OnSectionIndexChanged(int value)
    {
        if (value != 2)
        {
            CloseBackupsManager();
            CloseAbout();
        }
        if (value != 0)
        {
            CloseAddServer();
        }
        if (value != 1)
            CloseEditClient();
        else
            Clients.RefreshRunningState();
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

    private void ShowEditServer(ServerItemViewModel card)
    {
        if (_session is null || card.ServerId is null || !card.CanEdit || _adding is not null)
        {
            return;
        }

        var serversResult = _session.Service.ListServers(compatibleOnly: false);
        if (!serversResult.Succeeded)
        {
            return;
        }

        var entry = serversResult.Value!.FirstOrDefault(server => server.Id == card.ServerId);
        if (entry is null)
        {
            return;
        }

        SectionIndex = 0;
        _adding = new AddServerViewModel(Clients, _session, entry, () =>
        {
            Servers.ReloadFromService();
            CloseAddServer();
        }, CloseAddServer, _probe);
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

    private void ShowBackupsManager()
    {
        if (_session is null || _backups is not null)
            return;

        CloseAbout();
        _backups = new BackupsViewModel(_session, backRequested: CloseBackupsManager)
        {
            ConfirmDeleteBackupAsync = ConfirmDeleteBackupAsync,
            ResolveRestoreConflictAsync = ResolveRestoreConflictAsync,
        };
        _backups.PropertyChanged += OnSectionChanged;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private void CloseBackupsManager()
    {
        if (_backups is null)
            return;
        _backups.PropertyChanged -= OnSectionChanged;
        _backups = null;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private void ShowAbout()
    {
        if (_about is not null)
        {
            return;
        }

        CloseBackupsManager();
        _ = ShowAboutAsync();
    }

    private async Task ShowAboutAsync()
    {
        // License file/asset I/O stays off the UI thread; the result is cached for later opens.
        var licenses = await AboutDocuments.LoadThirdPartyLicensesAsync().ConfigureAwait(true);
        if (_about is not null || SectionIndex != 2)
        {
            return;
        }

        _about = new AboutViewModel(_uriOpener, CloseAbout, licenses);
        _about.PropertyChanged += OnSectionChanged;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private void CloseAbout()
    {
        if (_about is null)
            return;
        _about.PropertyChanged -= OnSectionChanged;
        _about = null;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(StatusText));
    }

    private async Task<bool> ConfirmDeleteBackupAsync(BackupEntry backup)
    {
        var dialog = new ConfirmDialogViewModel(
            "confirm.deletebackup.title", "confirm.deletebackup.body", "confirm.deletebackup.accept",
            "confirm.cancel", [backup.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), BackupDescription(backup)]);
        return await ConfirmAsync(dialog).ConfigureAwait(true);
    }

    private async Task<ConfirmDialogChoice> ResolveRestoreConflictAsync(BackupEntry backup)
    {
        var status = _session!.Service.GetStatus();
        var active = status.Succeeded ? status.Value!.ActiveByClient?.GetValueOrDefault(backup.ClientId)?.Name : null;
        active ??= status.Succeeded ? status.Value!.Active?.Name : null;
        var pendingBackups = _session.Service.ListBackups(backup.ClientId);
        var pending = pendingBackups.Succeeded ? pendingBackups.Value!.FirstOrDefault(entry => entry.IsPendingUndo) : null;
        if (pending is not { } pendingUndo)
        {
            var changedDialog = ConfirmDialogViewModel.BackupChanged(BackupDescription(backup));
            return await ChooseAsync(changedDialog).ConfigureAwait(true);
        }

        active = pendingUndo.ServerName ?? active;
        var pendingTime = pendingUndo.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var dialog = ConfirmDialogViewModel.BackupConflict(active ?? Loc.Get("backups.conflict.unknownserver"), pendingTime, BackupDescription(backup));
        return await ChooseAsync(dialog).ConfigureAwait(true);
    }

    private static string BackupDescription(BackupEntry backup) => backup.ServerName ?? backup.Label ?? Loc.Get("backups.item.manual");

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

    private async Task<bool> ConfirmDeleteServerAsync(ServerItemViewModel card)
    {
        var dialog = new ConfirmDialogViewModel(
            "confirm.deleteserver.title",
            card.IsActive ? "confirm.deleteserver.activebody" : "confirm.deleteserver.body",
            "confirm.deleteserver.accept",
            bodyArguments: [card.Name, card.Address]);
        return await ConfirmAsync(dialog).ConfigureAwait(true);
    }

    private void WireSections()
    {
        foreach (var section in _sections)
        {
            section.PropertyChanged += OnSectionChanged;
        }

        Servers.AddServerRequested += ShowAddServer;
        Servers.EditServerRequested += ShowEditServer;
        Servers.ConfirmDeleteServerAsync = ConfirmDeleteServerAsync;
        Loc.Instance.Changed += OnLanguageChanged;
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Servers) &&
            (e.PropertyName == nameof(ServersViewModel.GameClosed) ||
             e.PropertyName == nameof(ServersViewModel.Phase)))
        {
            Clients.RefreshRunningState();
        }

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
        _backups?.RefreshText();
        _about?.RefreshText();
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(Disclaimer));
        OnPropertyChanged(nameof(StatusText));
    }
}
