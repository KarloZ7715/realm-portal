using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;
using RealmPortal.App.Portal;
using RealmPortal.Core.Network;
using RealmPortal.App.Platform;
using RealmPortal.App.Settings;
using RealmPortal.Core;
using RealmPortal.Core.Service;
using RealmPortal.Core.ClientFiles;

namespace RealmPortal.App.ViewModels;

/// <summary>The window: the sidebar sections, the content area and the status bar.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ViewModelBase[] _sections;
    private readonly PortalSession? _session;
    private readonly IConnectionProbe? _probe;
    private readonly INetworkSettingsStore _network;
    private readonly ProbeScheduler _probes;
    private readonly IUpdateChecker _updates;
    private UpdateOffer? _pendingUpdateOffer;
    private readonly Func<Task<string?>>? _pickFolder;
    private AddServerViewModel? _adding;
    private EditClientViewModel? _editingClient;
    private BackupsViewModel? _backups;
    private AboutViewModel? _about;
    private int _aboutRequest;
    private readonly IUriOpener _uriOpener;

    public static MainViewModel ForDemo(DemoBackend? backend = null, bool autoTick = true, ISettingsStore? settings = null, IUriOpener? uriOpener = null) =>
        new(backend, autoTick, settings, uriOpener);

    public MainViewModel(DemoBackend? backend = null, bool autoTick = true, ISettingsStore? settings = null, IUriOpener? uriOpener = null)
    {
        _uriOpener = uriOpener ?? new ShellUriOpener();
        Backend = backend ?? new DemoBackend();
        _network = new MemoryNetworkSettingsStore();
        _probes = new ProbeScheduler(new TcpConnectionProbe());
        _updates = new PersistingUpdateChecker(new NullUpdateChecker(), _network);
        Servers = new ServersViewModel(Backend, autoTick);
        Clients = new ClientsViewModel(Backend);
        var settingsScreen = new SettingsViewModel(
            settings ?? new DemoSettingsStore(),
            () => Task.FromResult<string?>("/home/demo/.local/share/RealmPortal/backups-2"),
            _network,
            _updates);
        settingsScreen.ApplySavedLanguage();
        settingsScreen.CanManageBackups = false;
        settingsScreen.OpenBackupsManagerRequested += ShowBackupsManager;
        settingsScreen.OpenAboutRequested += ShowAbout;
        Settings = settingsScreen;
        _sections = [Servers, Clients, Settings];
        WireSections();
        Clients.ClientEditRequested += ShowEditClient;
        Clients.ConfirmRemoveAsync = ConfirmRemoveClientAsync;
        WireNetwork();
    }

    public MainViewModel(PortalSession session, Func<Task<string?>>? pickFolder = null, bool autoTick = true, IConnectionProbe? probe = null, Func<TimeSpan, Task>? delay = null, IUriOpener? uriOpener = null,
        INetworkSettingsStore? network = null, ProbeScheduler? probes = null, IUpdateChecker? updates = null)
    {
        _uriOpener = uriOpener ?? new ShellUriOpener();
        _session = session;
        _probe = probe ?? new TcpConnectionProbe();
        _pickFolder = pickFolder;
        _network = network ?? new PortalNetworkSettingsStore(session.Service);
        var connectionProbe = _probe ?? new TcpConnectionProbe();
        _probes = probes ?? new ProbeScheduler(connectionProbe);
        // Production: Core UpdateChecker (already uses NetworkSettingsStore.Update).
        // Injected doubles / PersistingUpdateChecker are used as-is (tests / demo).
        _updates = updates
            ?? new CoreUpdateCheckerAdapter(session.UpdateChecker);
        Servers = new ServersViewModel(session, autoTick, delay);
        Clients = new ClientsViewModel(session, pickFolder, startFirstScan: false);
        var settingsScreen = new SettingsViewModel(new PortalSettingsStore(session), pickFolder, _network, _updates);
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
        if (!session.UiPreferences.Load().IsWelcomeDone)
        {
            Welcome = new WelcomeViewModel(
                session,
                Clients,
                Servers,
                Settings,
                FinishWelcome,
                openAddServer: OpenAddServerFromWelcomeAsync);
        }
        else
        {
            Clients.StartFirstScanIfNeeded();
            ExceptionLog.Observe(RunAutomaticUpdateCheckAsync());
        }
        // After Welcome is assigned so SyncProbeListVisibility sees IsWelcomeActive.
        WireNetwork();
    }

    public DemoBackend? Backend { get; }

    public ServersViewModel Servers { get; }

    public ClientsViewModel Clients { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>The one-time first-run welcome, or null once finished or skipped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcomeActive), nameof(StatusText), nameof(Content))]
    private WelcomeViewModel? _welcome;

    public bool IsWelcomeActive => Welcome is not null;

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
                ExceptionLog.Observe(ConfirmLeaveSettingsAsync(value));
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
        if (IsWelcomeActive)
        {
            return;
        }

        if (_sectionIndex == 2 && Settings.IsDirty)
        {
            return;
        }

        SectionIndex = 1;
    }

    private void FinishWelcome()
    {
        Welcome = null;
        // Skip must not start a second scan while the welcome-driven discovery is still running;
        // that in-flight scan will persist FirstScanCompleted when it finishes.
        if (!Clients.IsScanning)
            Clients.StartFirstScanIfNeeded();
        // Servers already reload at construction, on the welcome servers step, and via ClientsChanged after scans.
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Content));
        SyncProbeListVisibility();
        ExceptionLog.Observe(RunAutomaticUpdateCheckAsync());
    }

    private async Task OpenAddServerFromWelcomeAsync()
    {
        // Welcome already cleared WelcomeCompleted; open the add-server form on the servers section.
        ShowAddServer();
        await Task.CompletedTask.ConfigureAwait(true);
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

    public string StatusText => Welcome?.StatusText ?? Content.StatusText;

    /// <summary>"ES · v1.0.0": the language toggle of the status bar (version from <see cref="AppInfo.Version"/>).</summary>
    public string LanguageLabel => $"{Loc.Instance.Code} · v{AppInfo.Version}";

    /// <summary>The non-affiliation note shown at the foot of the wide sidebar.</summary>
    public string Disclaimer => Loc.Instance.Current == Language.Es ? AppInfo.DisclaimerEs : AppInfo.DisclaimerEn;

    /// <summary>The confirm modal being shown, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModal))]
    private ConfirmDialogViewModel? _dialog;

    /// <summary>The update notice modal (#105), or null. Separate from <see cref="Dialog"/> so existing confirm flows stay typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModal))]
    private UpdateNoticeViewModel? _updateNotice;

    /// <summary>True when any modal (confirm or update notice) is open.</summary>
    public bool HasModal => Dialog is not null || UpdateNotice is not null;

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
        if (Dialog is not null || UpdateNotice is not null)
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

    /// <summary>Shows the update notice (#105) and returns the user's choice, or null if another dialog is open.</summary>
    public async Task<ConfirmDialogChoice?> ShowUpdateNoticeAsync(UpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (Dialog is not null || UpdateNotice is not null)
            return null;

        var dialog = new UpdateNoticeViewModel(offer);
        dialog.OpenUrlRequested += url =>
        {
            if (!ReleasePageUrl.TryGetTrusted(url, out var uri))
                return;
            ExceptionLog.Observe(_uriOpener.OpenAsync(uri));
        };
        DialogOpening?.Invoke();
        UpdateNotice = dialog;
        try
        {
            return await dialog.Choice;
        }
        finally
        {
            UpdateNotice = null;
            DialogClosed?.Invoke();
            TryPresentDeferredUpdate();
        }
    }


    private void WireNetwork()
    {
        Settings.StatusProbeEnabledChanged += enabled =>
        {
            _probes.SetEnabled(enabled);
            Servers.SetStatusDotsVisible(enabled);
        };
        Settings.PresentUpdateAsync = async offer => await ShowUpdateNoticeAsync(offer).ConfigureAwait(true);
        Settings.OpenExternalUrl = url =>
        {
            if (!ReleasePageUrl.TryGetTrusted(url, out var uri))
                return;
            ExceptionLog.Observe(_uriOpener.OpenAsync(uri));
        };

        // Memory store is sync (tests). Portal store takes ConfigDirectoryLock — load off the UI thread.
        if (_network is MemoryNetworkSettingsStore)
        {
            ApplyProbePrefs(_network.Load());
            return;
        }

        // Safe until prefs arrive: disabled before any NotifyListVisible(true).
        _probes.SetEnabled(false);
        Servers.AttachProbeScheduler(_probes, false);
        SyncProbeListVisibility();
        ExceptionLog.Observe(WireNetworkOffUiAsync());
    }

    private async Task WireNetworkOffUiAsync()
    {
        ServiceResult<NetworkSettings> loaded;
        try
        {
            loaded = await Task.Run(_network.Load).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExceptionLog.Default.Write(ex, "Main.WireNetwork");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyProbePrefs(loaded));
    }

    private void ApplyProbePrefs(ServiceResult<NetworkSettings> loaded)
    {
        var probeEnabled = loaded.Succeeded && loaded.Value!.StatusProbeEnabled;
        if (!loaded.Succeeded)
        {
            ExceptionLog.Default.WriteMessage(
                $"WireNetwork load failed: {loaded.Error}",
                "Main.WireNetwork");
        }

        // Seguridad: SetEnabled(status.probe_enabled) before NotifyListVisible(true).
        _probes.SetEnabled(probeEnabled);
        Servers.AttachProbeScheduler(_probes, probeEnabled);
        SyncProbeListVisibility();
    }

    /// <summary>Cancels in-flight probes and releases the scheduler (call when the main window closes).</summary>
    public void DisposeNetwork()
    {
        try
        {
            _probes.NotifyListVisible(false);
            _probes.SetEnabled(false);
            _probes.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void SyncProbeListVisibility()
    {
        // No probes until the servers list is the visible section and welcome is done.
        var visible = !IsWelcomeActive && SectionIndex == 0 && _adding is null;
        _probes.NotifyListVisible(visible);
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        try
        {
            var result = await _updates.CheckAsync(UpdateCheckKind.Automatic).ConfigureAwait(true);
            if (result.Kind != UpdateCheckResultKind.Offer || result.Offer is null)
                return;
            await PresentOrDeferUpdateAsync(result.Offer).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // Silent in the UI per docs/updates-and-status.md; still logged for diagnosis.
            ExceptionLog.Default.Write(ex, "Main.AutomaticUpdateCheck");
        }
    }

    private async Task PresentOrDeferUpdateAsync(UpdateOffer offer)
    {
        var choice = await ShowUpdateNoticeAsync(offer).ConfigureAwait(true);
        if (choice is null)
        {
            // Another modal is open — keep the offer for when it closes.
            _pendingUpdateOffer = offer;
            return;
        }

        _pendingUpdateOffer = null;
        if (choice == ConfirmDialogChoice.Confirm)
        {
            try
            {
                var outcome = await _updates.InstallUpdateAsync(offer).ConfigureAwait(true);
                await HandleInstallOutcomeAsync(outcome, offer).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExceptionLog.Default.Write(ex, "Main.InstallUpdate");
                await ConfirmAsync(new ConfirmDialogViewModel(
                    "update.install.failed.title",
                    "update.install.failed.body",
                    "dialog.ok")).ConfigureAwait(true);
            }
        }
        else if (choice == ConfirmDialogChoice.Secondary)
            await Task.Run(() => _updates.RemindLater(offer)).ConfigureAwait(true);
        else if (choice == ConfirmDialogChoice.Cancel)
            await Task.Run(() => _updates.SkipThisVersion(offer)).ConfigureAwait(true);
    }

    private async Task HandleInstallOutcomeAsync(UpdateInstallOutcome? outcome, UpdateOffer offer)
    {
        if (outcome is null)
            return;

        if (outcome.IsReleasePageFallback)
        {
            var url = outcome.SavedPath;
            if (!ReleasePageUrl.TryGetTrusted(url, out _))
                url = ReleasePageUrl.Resolve(offer);
            if (ReleasePageUrl.TryGetTrusted(url, out var uri))
                await _uriOpener.OpenAsync(uri).ConfigureAwait(true);
            return;
        }

        var detail = UpdateInstallPresentation.FormatDetail(outcome);
        if (string.IsNullOrWhiteSpace(detail))
            return;

        await ConfirmAsync(new ConfirmDialogViewModel(
            "update.install.title",
            "update.install.body",
            "dialog.ok",
            bodyArguments: [detail])).ConfigureAwait(true);
    }

    private void TryPresentDeferredUpdate()
    {
        if (_pendingUpdateOffer is null || Dialog is not null || UpdateNotice is not null)
            return;
        var offer = _pendingUpdateOffer;
        _pendingUpdateOffer = null;
        ExceptionLog.Observe(PresentOrDeferUpdateAsync(offer));
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
        SyncProbeListVisibility();
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
        SyncProbeListVisibility();
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
        SyncProbeListVisibility();
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
        SyncProbeListVisibility();
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
        var request = ++_aboutRequest;
        _ = ShowAboutAsync(request);
    }

    private async Task ShowAboutAsync(int request)
    {
        // License file/asset I/O stays off the UI thread; the result is cached for later opens.
        string licenses;
        try
        {
            licenses = await AboutDocuments.LoadThirdPartyLicensesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // Still open About; third-party text stays empty.
            licenses = string.Empty;
        }

        // Drop the result if the user left Settings, opened backups, or closed a prior About.
        if (request != _aboutRequest || _about is not null || SectionIndex != 2)
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
        _aboutRequest++;
        if (_about is null)
        {
            return;
        }

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
        Welcome?.RefreshText();
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
