using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;
using RealmPortal.App.Portal;
using RealmPortal.App.Service;
using RealmPortal.Core;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using StepState = RealmPortal.App.Controls.StepState;

namespace RealmPortal.App.ViewModels;

public enum ServersPhase
{
    Idle,
    Switching,
    Opening,
    Running,
}

/// <summary>One entry of the Servers header client filter: "All clients in use" or one saved client.</summary>
public sealed record ServerClientFilterOption(string? ClientId, string Label);

/// <summary>
/// The server list: search, switching, undo and play. Uses <see cref="PortalSession"/> in production and
/// <see cref="DemoBackend"/> for design-time screenshots and gallery tests.
/// </summary>
public sealed partial class ServersViewModel : ViewModelBase
{
    /// <summary>How long the undo notice stays.</summary>
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(8);

    /// <summary>Probe interval while waiting for the game process to appear after Play.</summary>
    public static readonly TimeSpan StartupProbeInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Probe interval after the game is confirmed running (backs off to cut CPU on long sessions).</summary>
    public static readonly TimeSpan RunningProbeInterval = TimeSpan.FromSeconds(4);


    private static readonly int[] SwitchProgress = [20, 55, 85];
    private static readonly int[] OpenProgress = [25, 60, 100];

    private readonly DemoBackend? _backend;
    private readonly PortalSession? _session;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly HashSet<string> _runningClients = new(StringComparer.Ordinal);
    private readonly List<ServerItemViewModel> _all = [];
    private readonly DispatcherTimer? _toastTimer;
    private ServerItemViewModel? _target;
    private ServerItemViewModel? _undoTo;
    private ServerItemViewModel? _undoFrom;
    private TimeSpan _toastElapsed;
    private bool _repairRun;
    private bool _undoConflict;
    private bool _canUndoPending;
    private ServiceError? _bannerError;
    private string? _bannerMessageKey;
    private IProbeScheduler? _probes;
    private bool _statusDotsVisible;
    private readonly Dictionary<string, ServerProbeSnapshot> _probeCache = new(StringComparer.Ordinal);

    public ServersViewModel(DemoBackend backend, bool autoTick = true)
    {
        _delay = Task.Delay;
        _backend = backend;
        var order = 0;
        foreach (var seed in DemoData.Servers)
        {
            var card = ServerItemViewModel.FromSeed(seed, item => ExceptionLog.Observe(PlayAsync(item)), item => ExceptionLog.Observe(SwitchOnlyAsync(item)));
            card.Order = order++;
            _all.Add(card);
        }

        if (autoTick)
        {
            _toastTimer = CreateToastTimer();
        }

        Refresh();
    }

    public ServersViewModel(PortalSession session, bool autoTick = true, Func<TimeSpan, Task>? delay = null)
    {
        _session = session;
        _delay = delay ?? Task.Delay;
        if (autoTick)
        {
            _toastTimer = CreateToastTimer();
        }

        ReloadFromService();
    }

    public bool IsPortal => _session is not null;

    /// <summary>Wires the login-port probe scheduler (#104). Safe to call once from Main.</summary>
    public void AttachProbeScheduler(IProbeScheduler scheduler, bool dotsVisible)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (_probes is not null)
            _probes.StatusChanged -= OnProbeStatusChanged;
        _probes = scheduler;
        _probes.StatusChanged += OnProbeStatusChanged;
        SetStatusDotsVisible(dotsVisible);
        PushServersToProbe();
    }

    public void SetStatusDotsVisible(bool visible)
    {
        _statusDotsVisible = visible;
        foreach (var card in _all)
            card.SetStatusDotVisible(visible);
        if (!visible)
        {
            foreach (var card in _all)
                card.ClearProbeStatus();
        }
        else
        {
            foreach (var card in _all)
            {
                if (card.ServerId is { } id && _probeCache.TryGetValue(id, out var snap))
                    card.ApplyProbeSnapshot(snap);
            }
        }
    }

    private void OnProbeStatusChanged(ServerProbeSnapshot snapshot)
    {
        // Probe callbacks arrive off the UI thread; touch cache + cards only on the UI thread.
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            ApplyProbeStatusOnUi(snapshot);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyProbeStatusOnUi(snapshot));
    }

    private void ApplyProbeStatusOnUi(ServerProbeSnapshot snapshot)
    {
        _probeCache[snapshot.ServerId] = snapshot;
        if (!_statusDotsVisible)
            return;
        var card = _all.FirstOrDefault(c => c.ServerId == snapshot.ServerId);
        card?.ApplyProbeSnapshot(snapshot);
    }

    private void PushServersToProbe()
    {
        if (_probes is null)
            return;

        var servers = new List<(string ServerId, string Host, int Port)>();
        foreach (var card in _all)
        {
            if (card.ServerId is null)
                continue;
            var token = string.IsNullOrWhiteSpace(card.Address) ? card.Host : card.Address;
            if (!RealmlistAddress.TryParseHostAddress(token, out var host, out var port))
                continue;
            servers.Add((card.ServerId, host, port));
        }
        _probes.SetServers(servers);

        foreach (var card in _all)
        {
            card.SetStatusDotVisible(_statusDotsVisible);
            if (_statusDotsVisible && card.ServerId is { } id && _probeCache.TryGetValue(id, out var snap))
                card.ApplyProbeSnapshot(snap);
            else if (!_statusDotsVisible)
                card.ClearProbeStatus();
        }
    }

    /// <summary>When undo conflicts, asks before <see cref="IPortalService.DiscardUndo"/> via <see cref="ConfirmDialogViewModel"/>.</summary>
    public Func<Task<bool>>? ConfirmDiscardAsync { get; set; }

    /// <summary>Asks the shell to confirm removing <paramref name="card"/> before <see cref="DeleteServerAsync"/> deletes it.</summary>
    public Func<ServerItemViewModel, Task<bool>>? ConfirmDeleteServerAsync { get; set; }

    /// <summary>Raised when the user picks "Edit server…" on a card; the shell opens the edit sheet.</summary>
    public event Action<ServerItemViewModel>? EditServerRequested;

    public ObservableCollection<ServerItemViewModel> Visible { get; } = [];

    /// <summary>Header filter items, rebuilt on every reload: "All clients in use" plus each saved client.</summary>
    public ObservableCollection<ServerClientFilterOption> ClientFilterOptions { get; } = [];

    public ObservableCollection<StepViewModel> Steps { get; } = [];

    public IReadOnlyList<ServerItemViewModel> All => _all;

    public ServerItemViewModel? Active => _all.FirstOrDefault(s => s.IsActive);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery), nameof(ResultsText), nameof(ShowEmpty), nameof(ShowResults), nameof(EmptyTitle))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsBusy), nameof(CanSearch), nameof(IsRunning), nameof(ShowBanner), nameof(ShowSteps), nameof(StatusText), nameof(ShowResults), nameof(ShowEmpty))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(DiscardUndoCommand))]
    private ServersPhase _phase;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerSeverity), nameof(BannerHeader), nameof(BannerBody), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _gameClosed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastText), nameof(StatusText), nameof(ShowDiscardUndo), nameof(ShowRetryUndo))]
    private bool _toastVisible;

    [ObservableProperty]
    private double _toastFraction = 1;

    [ObservableProperty]
    private ServerClientFilterOption? _selectedClientFilter;

    partial void OnSelectedClientFilterChanged(ServerClientFilterOption? value) => Refresh();

    private bool _hasMultipleClients;

    /// <summary>True when two or more clients are saved: cards then show their target client.</summary>
    public bool HasMultipleClients => _hasMultipleClients;

    /// <summary>True when two or more clients are marked "In use": the header filter is shown.</summary>
    public bool ShowClientFilter => ClientFilterOptions.Count > 2;

    /// <summary>How many clients are running a game right now (per-client lock).</summary>
    public int RunningCount => _runningClients.Count;

    public bool IsIdle => Phase == ServersPhase.Idle;

    public bool IsBusy => Phase is ServersPhase.Switching or ServersPhase.Opening;

    /// <summary>Search and Add stay available while a game runs, and hide only during a switch or launch.</summary>
    public bool CanSearch => !IsBusy;

    public bool IsRunning => Phase == ServersPhase.Running;

    public bool ShowBanner => Phase != ServersPhase.Idle || HasErrorBanner;

    public bool ShowSteps => IsBusy;

    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);

    public bool ShowResults => CanSearch && HasQuery && Visible.Count > 0;

    public bool ShowEmpty => CanSearch && HasQuery && Visible.Count == 0;

    public bool ShowDiscardUndo => ToastVisible && _undoConflict;

    /// <summary>Conflict toast: run <see cref="UndoAsync"/> again after the client files are fixed. Pending undo stays.</summary>
    public bool ShowRetryUndo => ToastVisible && _undoConflict;

    /// <summary>True while the service still has a pending undo, including after the toast expires, play, or a restart.</summary>
    public bool CanUndoPending => _canUndoPending;

    public string ResultsText => Visible.Count == 1
        ? Loc.Format("servers.results.one", SearchText.Trim())
        : Loc.Format("servers.results.many", Visible.Count, SearchText.Trim());

    public string EmptyTitle => Loc.Format("servers.empty.title", SearchText.Trim());

    public BannerSeverity BannerSeverity => HasErrorBanner
        ? BannerSeverity.Warning
        : Phase switch
        {
            ServersPhase.Running when !GameClosed && !MultipleRunning => BannerSeverity.Warning,
            _ => BannerSeverity.Info,
        };

    public string BannerHeader => HasErrorBanner
        ? _bannerError is { } error && PlayServiceResults.SwitchWasApplied(error)
            ? ErrorText.Title(error)
            : Loc.Get(_bannerMessageKey == "switch.cache.clear.warning" ? "banner.warning.title" : "error.banner.title")
        : Phase switch
        {
            ServersPhase.Switching when _repairRun => Loc.Format("repair.title", _target?.Name),
            ServersPhase.Switching => Loc.Format("switch.title", _target?.Name),
            ServersPhase.Opening => Loc.Get("open.title"),
            ServersPhase.Running => Loc.Get(GameClosed ? "run.closed.title" : "run.title"),
            _ => string.Empty,
        };

    public string BannerBody => ErrorBannerText ?? Phase switch
    {
        ServersPhase.Switching when _repairRun => Loc.Get("repair.body"),
        ServersPhase.Switching => Loc.Get("switch.body"),
        ServersPhase.Opening => Loc.Format("open.body", _target?.Name, _target?.ClientVersion),
        ServersPhase.Running when GameClosed => Loc.Get("run.closed.body"),
        ServersPhase.Running when MultipleRunning => Loc.Format("phase.running.multiple", _runningClients.Count, RunningSummary()),
        ServersPhase.Running => Loc.Get("run.body"),
        _ => string.Empty,
    };

    public string ToastText => _undoConflict
        ? Loc.Get("toast.undoconflict")
        : _undoTo is null ? string.Empty : Loc.Format("toast.switched", _undoTo.Name);

    public override string StatusText => ErrorBannerText is not null
        ? ErrorBannerText
        : Phase switch
        {
            ServersPhase.Switching when _repairRun => Loc.Format("status.repairing", _target?.Name),
            ServersPhase.Switching => Loc.Format("status.switching", _target?.Name),
            ServersPhase.Opening => Loc.Get("status.opening"),
            ServersPhase.Running when GameClosed => Loc.Get("status.closed"),
            ServersPhase.Running when MultipleRunning => Loc.Format("phase.running.multiple", _runningClients.Count, RunningSummary()),
            ServersPhase.Running => Loc.Format("status.running", _target?.Name),
            _ => ToastVisible && Active is { } active
                ? Loc.Format("status.active", active.Name)
                : Loc.Format("status.realmlist", Active?.Host ?? "-"),
        };

    private bool MultipleRunning => _runningClients.Count >= 2;

    /// <summary>"Server (Client)" list of the cards currently marked as running, for the multi-game banner.</summary>
    private string RunningSummary() => string.Join(", ", _all
        .Where(s => s.Overlay == ServerOverlay.Running)
        .Select(s => s.ClientDisplayName is { Length: > 0 } client ? $"{s.Name} ({client})" : s.Name));

    public ServerItemViewModel? SelectedCard
    {
        get => Active;
        set => OnPropertyChanged(nameof(SelectedCard));
    }

    public ServerItemViewModel? Target => _target;

    public event Action? AddServerRequested;

    [RelayCommand]
    private void RequestAddServer() => AddServerRequested?.Invoke();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void EditRequested(ServerItemViewModel item) => EditServerRequested?.Invoke(item);

    private void DeleteRequested(ServerItemViewModel item) => ExceptionLog.Observe(DeleteServerAsync(item));

    /// <summary>Asks the shell to confirm, then removes the server (with its active record) and reloads the list.</summary>
    public async Task DeleteServerAsync(ServerItemViewModel card)
    {
        if (_session is null || card.ServerId is null || !card.CanDelete)
        {
            return;
        }

        var confirm = ConfirmDeleteServerAsync;
        if (confirm is not null && !await confirm(card).ConfigureAwait(true))
        {
            return;
        }

        var result = await Task.Run(() => _session.Service.RemoveServer(card.ServerId)).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            SetError(result.Error);
            return;
        }

        HideToast();
        ClearErrorBanner();
        ReloadFromService();
    }

    [RelayCommand(CanExecute = nameof(GameClosed))]
    private void Retry() => Finish();

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_session is not null)
        {
            var result = await Task.Run(() => _session.Service.Undo()).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!result.Succeeded)
                {
                    if (result.Error!.Code == ServiceErrorCode.UndoConflict)
                    {
                        _undoConflict = true;
                        _bannerError = result.Error;
                        _bannerMessageKey = null;
                        ShowToast();
                        Refresh();
                    }
                    else
                    {
                        SetError(result.Error);
                    }
                }
                else
                {
                    _undoConflict = false;
                    ClearErrorBanner();
                    HideToast();
                    ReloadFromService();
                }

                RefreshUndoAvailability();
            });
            return;
        }

        if (_undoFrom is null || _undoTo is null)
        {
            return;
        }

        _undoTo.IsActive = false;
        _undoFrom.IsActive = true;
        HideToast();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanDiscardUndo))]
    private async Task DiscardUndoAsync()
    {
        if (_session is null)
        {
            HideToast();
            return;
        }

        var confirm = ConfirmDiscardAsync ?? (() => Task.FromResult(true));
        if (!await confirm().ConfigureAwait(true))
        {
            return;
        }

        var result = await Task.Run(() => _session.Service.DiscardUndo()).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!result.Succeeded)
            {
                SetError(result.Error);
            }
            else
            {
                _undoConflict = false;
                ClearErrorBanner();
                HideToast();
                ReloadFromService();
            }

            RefreshUndoAvailability();
        });
    }

    private bool CanUndo() => IsIdle && (_session is not null
        ? _canUndoPending
        : _undoFrom is not null && _undoTo is not null);

    /// <summary>Persistent bar and conflict toast can discard only while no switch or play is running.</summary>
    private bool CanDiscardUndo() => IsIdle;

    /// <summary>Reads <see cref="PortalStatus.CanUndo"/> after a reload, an operation, or startup.</summary>
    private void RefreshUndoAvailability()
    {
        var canUndo = _session?.StatusOrNull()?.CanUndo == true;
        if (_canUndoPending != canUndo)
        {
            _canUndoPending = canUndo;
            OnPropertyChanged(nameof(CanUndoPending));
        }

        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void DismissToast()
    {
        // Closing the notice only hides it. A pending undo, including one stopped on a conflict, stays until Undo or Discard.
        HideToast();
    }

    public void AdvanceToast(TimeSpan elapsed)
    {
        if (!ToastVisible || _undoConflict)
        {
            return;
        }

        _toastElapsed += elapsed;
        ToastFraction = Math.Max(0, 1 - _toastElapsed / ToastDuration);
        if (_toastElapsed >= ToastDuration)
        {
            HideToast();
        }
    }

    public ServerItemViewModel AddServer(ServerSeed seed)
    {
        var item = ServerItemViewModel.FromSeed(seed, i => ExceptionLog.Observe(PlayAsync(i)), i => ExceptionLog.Observe(SwitchOnlyAsync(i)));
        item.Order = _all.Count == 0 ? 0 : _all.Max(s => s.Order) + 1;
        _all.Add(item);
        SearchText = string.Empty;
        Refresh();
        return item;
    }

    public void ReloadFromService()
    {
        if (_session is null)
        {
            return;
        }

        var clientsResult = _session.Service.ListClients();
        var clients = clientsResult.Succeeded ? clientsResult.Value! : [];
        var clientsById = new Dictionary<string, ClientEntry>(clients.Count, StringComparer.Ordinal);
        foreach (var client in clients)
        {
            clientsById.TryAdd(client.Id, client);
        }

        var list = _session.Service.ListServers(compatibleOnly: false);
        if (!list.Succeeded)
        {
            SetError(list.Error);
            RefreshUndoAvailability();
            return;
        }

        var status = _session.StatusOrNull();
        var projectionInSync = status?.ProjectionInSync ?? true;

        var inUseIds = _session.UiPreferences.Load().InUseSet;
        var inUseClients = clients.Where(c => inUseIds.Contains(c.Id)).ToList();
        _hasMultipleClients = clients.Count >= 2;
        RebuildClientFilter(inUseClients);

        _all.Clear();
        _runningClients.Clear();
        foreach (var clientId in clients.Select(client => client.Id).Distinct(StringComparer.Ordinal))
        {
            var running = _session.Service.IsGameRunning(clientId);
            if (running.Succeeded && running.Value)
                _runningClients.Add(clientId);
        }

        var order = 0;
        foreach (var entry in list.Value!)
        {
            var clientId = _session.ResolveClientId(clients, entry.ClientId);
            var clientVersion = clientId is not null && clientsById.TryGetValue(clientId, out var client)
                ? client.Version
                : null;
            var clientDisplayName = clientId is not null && clientsById.TryGetValue(clientId, out var resolvedClient)
                ? ClientDisplayName(resolvedClient)
                : null;
            var compatibleIds = clients
                .Where(c => c.Version is null || CompatibilityFilter.IsCompatible(entry, c.Version))
                .Select(c => c.Id)
                .ToHashSet(StringComparer.Ordinal);
            var card = ServerItemViewModel.FromEntry(
                entry,
                clientVersion,
                projectionInSync,
                item => ExceptionLog.Observe(PlayAsync(item)),
                item => ExceptionLog.Observe(SwitchOnlyAsync(item)),
                clientId,
                compatibleIds,
                _hasMultipleClients,
                clientDisplayName,
                EditRequested,
                DeleteRequested);
            card.Order = order++;
            if (clientId is not null && _runningClients.Contains(clientId))
                card.Overlay = ServerOverlay.Running;
            _all.Add(card);
        }

        OnPropertyChanged(nameof(HasMultipleClients));
        Refresh();
        RefreshUndoAvailability();
        PushServersToProbe();
    }

    private static string ClientDisplayName(ClientEntry client) =>
        string.IsNullOrWhiteSpace(client.Name) ? VersionText.Of(client.Version) : client.Name!;

    /// <summary>"Name · 3.3.5a (12340)", or just the version when the client has no name, so it never shows twice.</summary>
    private static string ClientFilterLabel(ClientEntry client) =>
        string.IsNullOrWhiteSpace(client.Name)
            ? ClientDisplayName(client)
            : VersionText.WithName(client.Name!, VersionText.Of(client.Version));

    /// <summary>Rebuilds the header filter: "All clients in use" followed by the clients marked in use.</summary>
    private void RebuildClientFilter(IReadOnlyList<ClientEntry> inUseClients)
    {
        var keepId = SelectedClientFilter?.ClientId;
        ClientFilterOptions.Clear();
        ClientFilterOptions.Add(new ServerClientFilterOption(null, Loc.Get("servers.clientfilter.all")));
        foreach (var client in inUseClients)
        {
            ClientFilterOptions.Add(new ServerClientFilterOption(client.Id, ClientFilterLabel(client)));
        }

        SelectedClientFilter = ClientFilterOptions.FirstOrDefault(o => o.ClientId == keepId) ?? ClientFilterOptions[0];
        OnPropertyChanged(nameof(ShowClientFilter));
    }

    public Task SwitchOnlyAsync(ServerItemViewModel target)
    {
        target = ResolveCard(target);
        return _session is not null
            ? RunPortalAsync(target, play: false)
            : RunDemoAsync(target, ServersPhase.Switching, [StepKind.Copy, StepKind.Realmlist, StepKind.Account], SwitchProgress);
    }

    public Task PlayAsync(ServerItemViewModel target)
    {
        target = ResolveCard(target);
        return _session is not null
            ? RunPortalAsync(target, play: true)
            : RunDemoAsync(target, ServersPhase.Opening, [StepKind.Realmlist, StepKind.Account, StepKind.Launch], OpenProgress);
    }

    private ServerItemViewModel ResolveCard(ServerItemViewModel target)
    {
        if (target.ServerId is null)
        {
            return target;
        }

        return _all.FirstOrDefault(s => s.ServerId == target.ServerId) ?? target;
    }

    private async Task RunPortalAsync(ServerItemViewModel target, bool play)
    {
        if (IsBusy || !target.CanPlay || target.ServerId is null || _session is null)
        {
            return;
        }

        // A pinned server keeps its client. An unpinned server uses the header filter's client when one is selected.
        var preferredId = target.PreferredClientId;
        if (preferredId is null && SelectedClientFilter?.ClientId is { } filterId
            && target.CompatibleClientIds.Contains(filterId))
        {
            preferredId = filterId;
        }

        var clientId = _session.ResolveClientId(preferredId);
        if (clientId is null)
        {
            SetError(null, "error.noclient.body");
            return;
        }

        HideToast();
        ClearErrorBanner();
        _repairRun = target.NeedsRepair;
        _target = target;
        SearchText = string.Empty;
        Steps.Clear();
        var kinds = play ? ServiceProgressMapper.PlaySteps : ServiceProgressMapper.SwitchSteps;
        for (var i = 0; i < kinds.Length; i++)
        {
            Steps.Add(new StepViewModel(i + 1, kinds[i], i == kinds.Length - 1));
        }

        Progress = 0;
        target.Overlay = play ? ServerOverlay.Opening : ServerOverlay.Switching;
        Phase = play ? ServersPhase.Opening : ServersPhase.Switching;
        Refresh();

        // Progress<T> is created on the UI thread before Task.Run, so callbacks are posted to its SynchronizationContext.
        var progress = new Progress<ServiceProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var percent = Progress;
                ServiceProgressMapper.ApplyProgress(p, Steps, kinds, ref percent);
                Progress = percent;
                OnPropertyChanged(nameof(BannerHeader));
                OnPropertyChanged(nameof(BannerBody));
                OnPropertyChanged(nameof(StatusText));
            });
        });

        var request = new SwitchRequest(clientId, target.ServerId);
        var result = await Task.Run(() =>
            play ? _session.Service.Play(request, progress) : _session.Service.SwitchServer(request, progress)).ConfigureAwait(true);

        if (!result.Succeeded)
        {
            var appliedLaunch = play && PlayServiceResults.ShouldRefreshStatusAfterFailure(result);
            SetError(result.Error);
            target.Overlay = ServerOverlay.None;
            Phase = ServersPhase.Idle;
            _repairRun = false;
            _target = null;
            ReloadFromService();
            if (appliedLaunch && result.Error is { } launchError && PlayServiceResults.UndoRemainsAvailable(launchError)
                && _session.StatusOrNull()?.CanUndo == true)
            {
                var activeId = _session.StatusOrNull()?.Active?.Id;
                _undoTo = activeId is null ? null : _all.FirstOrDefault(s => s.ServerId == activeId);
                ShowToast();
            }

            return;
        }

        var outcome = result.Value!;
        var previous = outcome.Previous;
        if (outcome.CacheClearWarning is not null)
            SetError(null, "switch.cache.clear.warning");

        if (!play)
        {
            target.Overlay = ServerOverlay.None;
            // Keep the running state when another client is still playing; only the last one returns to Idle.
            Phase = _runningClients.Count > 0 ? ServersPhase.Running : ServersPhase.Idle;
            _repairRun = false;
            Progress = 100;
            ReloadFromService();
            if (_session.StatusOrNull()?.CanUndo == true)
            {
                _undoTo = _all.FirstOrDefault(s => s.ServerId == outcome.Active.Id);
                _undoFrom = outcome.Previous is null
                    ? null
                    : _all.FirstOrDefault(s => s.ServerId == outcome.Previous.Id);
                ShowToast();
            }

            return;
        }

        GameClosed = false;
        Phase = ServersPhase.Running;
        // Reload replaces every card. The running badge has to land on the new instance.
        ReloadFromService();
        _runningClients.Add(clientId);
        target = ResolveCard(target);
        _target = target;
        target.Overlay = ServerOverlay.Running;
        Refresh();
        ExceptionLog.Observe(WatchGamePortalAsync(clientId));
    }

    private async Task RunDemoAsync(ServerItemViewModel target, ServersPhase phase, StepKind[] kinds, int[] progress)
    {
        if (Phase != ServersPhase.Idle || !target.CanPlay || _backend is null)
        {
            return;
        }

        HideToast();
        var previous = Active;
        _target = target;
        SearchText = string.Empty;
        Steps.Clear();
        for (var i = 0; i < kinds.Length; i++)
        {
            Steps.Add(new StepViewModel(i + 1, kinds[i], i == kinds.Length - 1));
        }

        Progress = 0;
        target.Overlay = phase == ServersPhase.Switching ? ServerOverlay.Switching : ServerOverlay.Opening;
        Phase = phase;
        Refresh();

        for (var i = 0; i < kinds.Length; i++)
        {
            Steps[i].State = StepState.Current;
            Progress = progress[i];
            await _backend.RunStepAsync(kinds[i]).ConfigureAwait(true);
            Steps[i].State = StepState.Done;
        }

        if (previous is not null)
        {
            previous.IsActive = false;
        }

        target.IsActive = true;
        if (phase == ServersPhase.Switching)
        {
            Progress = 100;
            target.Overlay = ServerOverlay.None;
            _undoFrom = previous;
            _undoTo = target;
            Phase = ServersPhase.Idle;
            if (previous is not null && previous != target)
            {
                ShowToast();
            }

            Refresh();
            return;
        }

        target.Overlay = ServerOverlay.Running;
        GameClosed = false;
        Phase = ServersPhase.Running;
        _backend.StartGame();
        Refresh();
        ExceptionLog.Observe(WatchGameAsync());
    }

    private async Task WatchGameAsync()
    {
        if (_backend is null)
        {
            return;
        }

        await _backend.GameExited.ConfigureAwait(true);
        if (Phase == ServersPhase.Running)
        {
            GameClosed = true;
            Refresh();
        }
    }

    private async Task WatchGamePortalAsync(string clientId)
    {
        if (_session is null)
        {
            return;
        }

        var elapsed = TimeSpan.Zero;
        var sawRunning = false;
        var consecutiveMisses = 0;
        const int closeMisses = 3;
        // Fast while waiting for the process to appear; back off once it is confirmed running.
        var interval = StartupProbeInterval;
        var startupTimeout = TimeSpan.FromSeconds(30);
        while (_runningClients.Contains(clientId))
        {
            var running = await Task.Run(() => _session.Service.IsGameRunning(clientId)).ConfigureAwait(true);
            if (!running.Succeeded)
            {
                // Drop back so another play or switch can start, and keep the error banner up.
                MarkClientClosed(clientId);
                Finish();
                SetError(running.Error);
                return;
            }

            if (!running.Value)
            {
                if (sawRunning && ++consecutiveMisses >= closeMisses)
                {
                    MarkClientClosed(clientId);
                    return;
                }
                if (!sawRunning && elapsed >= startupTimeout)
                {
                    MarkClientClosed(clientId);
                    return;
                }
            }
            else
            {
                if (!sawRunning)
                {
                    sawRunning = true;
                    interval = RunningProbeInterval;
                }

                consecutiveMisses = 0;
                if (_runningClients.Add(clientId))
                {
                    OnPropertyChanged(nameof(RunningCount));
                    Refresh();
                }
            }

            await _delay(interval).ConfigureAwait(true);
            elapsed += interval;
        }
    }

    /// <summary>
    /// Marks one client's game as closed. The closed banner (and its Retry) appears only once the last
    /// game closes; a game still open on another client keeps that client's card locked and the rest usable.
    /// </summary>
    private void MarkClientClosed(string clientId)
    {
        if (!_runningClients.Remove(clientId))
        {
            return;
        }

        foreach (var card in _all.Where(c => c.EffectiveClientId == clientId))
        {
            card.Overlay = ServerOverlay.None;
        }

        GameClosed = _runningClients.Count == 0;
        Refresh();
    }

    private void Finish()
    {
        if (_session is not null && _target?.EffectiveClientId is { } clientId)
        {
            var running = _session.Service.IsGameRunning(clientId);
            if (running.Succeeded && running.Value)
            {
                _runningClients.Add(clientId);
                GameClosed = false;
                Phase = ServersPhase.Running;
                Refresh();
                return;
            }
            _runningClients.Remove(clientId);
        }
        if (_target is not null)
        {
            _target.Overlay = ServerOverlay.None;
        }

        _target = null;
        GameClosed = false;
        Phase = _runningClients.Count > 0 ? ServersPhase.Running : ServersPhase.Idle;
        Refresh();
    }

    /// <summary>True when an error banner is showing. The copy itself is <see cref="ErrorBannerText"/>.</summary>
    private bool HasErrorBanner => _bannerError is not null || _bannerMessageKey is not null;

    /// <summary>
    /// Error banner copy resolved at read time, so a language change updates <see cref="BannerBody"/> and <see cref="StatusText"/>.
    /// Service failures, including undo conflict, keep <see cref="_bannerError"/>. Message-only failures keep a localization key
    /// (<c>error.unknown.body</c> when the failure has neither).
    /// </summary>
    private string? ErrorBannerText => _bannerError is { } error
        ? ErrorText.Body(error)
        : _bannerMessageKey is null ? null : Loc.Get(_bannerMessageKey);

    /// <summary>
    /// Shows the error banner from <paramref name="error"/>, or from <paramref name="messageKey"/> when there is no error.
    /// Both null uses <c>error.unknown.body</c>. Text is formatted when the banner is read.
    /// </summary>
    private void SetError(ServiceError? error, string? messageKey = null)
    {
        _bannerError = messageKey is null ? error : null;
        _bannerMessageKey = messageKey ?? (error is null ? "error.unknown.body" : null);
        OnPropertyChanged(nameof(BannerHeader));
        OnPropertyChanged(nameof(BannerBody));
        OnPropertyChanged(nameof(BannerSeverity));
        OnPropertyChanged(nameof(ShowBanner));
        OnPropertyChanged(nameof(StatusText));
    }

    private void ClearErrorBanner()
    {
        _bannerError = null;
        _bannerMessageKey = null;
    }

    private void ShowToast()
    {
        _toastElapsed = TimeSpan.Zero;
        ToastFraction = 1;
        ToastVisible = true;
        OnPropertyChanged(nameof(ToastText));
        OnPropertyChanged(nameof(ShowDiscardUndo));
        OnPropertyChanged(nameof(ShowRetryUndo));
        RefreshUndoAvailability();
        if (!_undoConflict)
        {
            _toastTimer?.Start();
        }
    }

    private void HideToast()
    {
        _toastTimer?.Stop();
        ToastVisible = false;
        _undoFrom = null;
        _undoTo = null;
        _undoConflict = false;
        OnPropertyChanged(nameof(ToastText));
        OnPropertyChanged(nameof(ShowDiscardUndo));
        OnPropertyChanged(nameof(ShowRetryUndo));
        RefreshUndoAvailability();
    }

    private DispatcherTimer CreateToastTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => AdvanceToast(timer.Interval);
        return timer;
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    public override void RefreshText()
    {
        foreach (var item in _all)
        {
            item.RefreshText();
        }

        foreach (var step in Steps)
        {
            step.RefreshText();
        }

        OnPropertyChanged(nameof(ResultsText));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(BannerHeader));
        OnPropertyChanged(nameof(BannerBody));
        OnPropertyChanged(nameof(ToastText));
        OnPropertyChanged(nameof(StatusText));
    }

    private void Refresh()
    {
        var query = SearchText.Trim();
        var filterClientId = SelectedClientFilter?.ClientId;
        var matches = _all
            .Where(s => query.Length == 0 || s.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Where(s => filterClientId is null || s.CompatibleClientIds.Contains(filterClientId))
            .OrderBy(s => s == _target ? 0 : s.IsActive ? 1 : 2)
            .ThenBy(s => s.Order)
            .ToList();

        if (!Visible.SequenceEqual(matches))
        {
            Visible.Clear();
            foreach (var match in matches)
            {
                Visible.Add(match);
            }
        }

        foreach (var item in _all)
        {
            // A card is locked only by an in-flight switch/launch or by its own client already running a game.
            // Cards on idle clients stay usable, so another client can be switched and launched concurrently.
            var runningClient = item.EffectiveClientId is { } clientId && _runningClients.Contains(clientId);
            item.CanAct = !IsBusy && !runningClient;
            item.SetLock(
                runningClient ? ServerCardLock.ClientRunning
                : item.IsCompatible ? ServerCardLock.None
                : ServerCardLock.NoInUseClient,
                item.ClientDisplayName);
        }

        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(SelectedCard));
        OnPropertyChanged(nameof(ResultsText));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(BannerSeverity));
        OnPropertyChanged(nameof(BannerHeader));
        OnPropertyChanged(nameof(BannerBody));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowBanner));
        OnPropertyChanged(nameof(RunningCount));
    }
}
