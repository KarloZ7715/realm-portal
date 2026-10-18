using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Service;
using RealmPortal.Core.Service;
using StepState = RealmPortal.App.Controls.StepState;

namespace RealmPortal.App.ViewModels;

public enum ServersPhase
{
    Idle,
    Switching,
    Opening,
    Running,
}

/// <summary>
/// The server list: search, switching, undo and play. Uses <see cref="PortalSession"/> in production and
/// <see cref="DemoBackend"/> for design-time screenshots and gallery tests.
/// </summary>
public sealed partial class ServersViewModel : ViewModelBase
{
    /// <summary>How long the undo notice stays.</summary>
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(8);

    private static readonly int[] SwitchProgress = [20, 55, 85];
    private static readonly int[] OpenProgress = [25, 60, 100];

    private readonly DemoBackend? _backend;
    private readonly PortalSession? _session;
    private readonly List<ServerItemViewModel> _all = [];
    private readonly DispatcherTimer? _toastTimer;
    private ServerItemViewModel? _target;
    private ServerItemViewModel? _undoTo;
    private ServerItemViewModel? _undoFrom;
    private TimeSpan _toastElapsed;
    private bool _repairRun;
    private bool _undoConflict;
    private string? _errorBanner;
    private ServiceError? _bannerError;

    public ServersViewModel(DemoBackend backend, bool autoTick = true)
    {
        _backend = backend;
        var order = 0;
        foreach (var seed in DemoData.Servers)
        {
            var card = ServerItemViewModel.FromSeed(seed, item => _ = PlayAsync(item), item => _ = SwitchOnlyAsync(item));
            card.Order = order++;
            _all.Add(card);
        }

        if (autoTick)
        {
            _toastTimer = CreateToastTimer();
        }

        Refresh();
    }

    public ServersViewModel(PortalSession session, bool autoTick = true)
    {
        _session = session;
        if (autoTick)
        {
            _toastTimer = CreateToastTimer();
        }

        ReloadFromService();
    }

    public bool IsPortal => _session is not null;

    /// <summary>When undo conflicts, asks before <see cref="IPortalService.DiscardUndo"/> via <see cref="ConfirmDialogViewModel"/>.</summary>
    public Func<Task<bool>>? ConfirmDiscardAsync { get; set; }

    public ObservableCollection<ServerItemViewModel> Visible { get; } = [];

    public ObservableCollection<StepViewModel> Steps { get; } = [];

    public IReadOnlyList<ServerItemViewModel> All => _all;

    public ServerItemViewModel? Active => _all.FirstOrDefault(s => s.IsActive);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery), nameof(ResultsText), nameof(ShowEmpty), nameof(ShowResults), nameof(EmptyTitle))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsBusy), nameof(IsRunning), nameof(ShowBanner), nameof(ShowSteps), nameof(StatusText), nameof(ShowResults), nameof(ShowEmpty))]
    private ServersPhase _phase;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerSeverity), nameof(BannerHeader), nameof(BannerBody), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _gameClosed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastText), nameof(StatusText), nameof(ShowDiscardUndo))]
    private bool _toastVisible;

    [ObservableProperty]
    private double _toastFraction = 1;

    public bool IsIdle => Phase == ServersPhase.Idle;

    public bool IsBusy => Phase is ServersPhase.Switching or ServersPhase.Opening;

    public bool IsRunning => Phase == ServersPhase.Running;

    public bool ShowBanner => Phase != ServersPhase.Idle || _errorBanner is not null;

    public bool ShowSteps => IsBusy;

    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);

    public bool ShowResults => IsIdle && HasQuery && Visible.Count > 0;

    public bool ShowEmpty => IsIdle && HasQuery && Visible.Count == 0;

    public bool ShowDiscardUndo => ToastVisible && _undoConflict;

    public string ResultsText => Visible.Count == 1
        ? Loc.Format("servers.results.one", SearchText.Trim())
        : Loc.Format("servers.results.many", Visible.Count, SearchText.Trim());

    public string EmptyTitle => Loc.Format("servers.empty.title", SearchText.Trim());

    public BannerSeverity BannerSeverity => _errorBanner is not null
        ? BannerSeverity.Warning
        : Phase switch
        {
            ServersPhase.Running when !GameClosed => BannerSeverity.Warning,
            _ => BannerSeverity.Info,
        };

    public string BannerHeader => _errorBanner is not null
        ? _bannerError is { } error && PlayServiceResults.SwitchWasApplied(error)
            ? ErrorText.Title(error)
            : Loc.Get("error.banner.title")
        : Phase switch
        {
            ServersPhase.Switching when _repairRun => Loc.Format("repair.title", _target?.Name),
            ServersPhase.Switching => Loc.Format("switch.title", _target?.Name),
            ServersPhase.Opening => Loc.Get("open.title"),
            ServersPhase.Running => Loc.Get(GameClosed ? "run.closed.title" : "run.title"),
            _ => string.Empty,
        };

    public string BannerBody => _errorBanner ?? Phase switch
    {
        ServersPhase.Switching when _repairRun => Loc.Get("repair.body"),
        ServersPhase.Switching => Loc.Get("switch.body"),
        ServersPhase.Opening => Loc.Format("open.body", _target?.Name, _target?.ClientVersion),
        ServersPhase.Running => Loc.Get(GameClosed ? "run.closed.body" : "run.body"),
        _ => string.Empty,
    };

    public string ToastText => _undoConflict
        ? Loc.Get("toast.undoconflict")
        : _undoTo is null ? string.Empty : Loc.Format("toast.switched", _undoTo.Name);

    public override string StatusText => _errorBanner is not null
        ? _errorBanner
        : Phase switch
        {
            ServersPhase.Switching when _repairRun => Loc.Format("status.repairing", _target?.Name),
            ServersPhase.Switching => Loc.Format("status.switching", _target?.Name),
            ServersPhase.Opening => Loc.Get("status.opening"),
            ServersPhase.Running => GameClosed ? Loc.Get("status.closed") : Loc.Format("status.running", _target?.Name),
            _ => ToastVisible && Active is { } active
                ? Loc.Format("status.active", active.Name)
                : Loc.Format("status.realmlist", Active?.Host ?? "-"),
        };

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

    [RelayCommand(CanExecute = nameof(GameClosed))]
    private void Retry() => Finish();

    [RelayCommand]
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
                        _bannerError = null;
                        _errorBanner = ErrorText.Body(result.Error);
                        ShowToast();
                        Refresh();
                    }
                    else
                    {
                        SetError(result.Error);
                    }

                    return;
                }

                _undoConflict = false;
                _bannerError = null;
                _errorBanner = null;
                HideToast();
                ReloadFromService();
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

    [RelayCommand]
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
                return;
            }

            _undoConflict = false;
            _bannerError = null;
            _errorBanner = null;
            HideToast();
            ReloadFromService();
        });
    }

    [RelayCommand]
    private void DismissToast()
    {
        if (!_undoConflict)
        {
            HideToast();
        }
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
        var item = ServerItemViewModel.FromSeed(seed, i => _ = PlayAsync(i), i => _ = SwitchOnlyAsync(i));
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
            return;
        }

        var status = _session.StatusOrNull();
        var projectionInSync = status?.ProjectionInSync ?? true;

        _all.Clear();
        var order = 0;
        foreach (var entry in list.Value!)
        {
            var clientId = _session.ResolveClientId(clients, entry.ClientId);
            var clientVersion = clientId is not null && clientsById.TryGetValue(clientId, out var client)
                ? client.Version
                : null;
            var card = ServerItemViewModel.FromEntry(
                entry,
                clientVersion,
                projectionInSync,
                item => _ = PlayAsync(item),
                item => _ = SwitchOnlyAsync(item));
            card.Order = order++;
            _all.Add(card);
        }

        Refresh();
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
        if (Phase != ServersPhase.Idle || !target.CanPlay || target.ServerId is null || _session is null)
        {
            return;
        }

        var clientId = _session.ResolveClientId(target.PreferredClientId);
        if (clientId is null)
        {
            SetError(null, Loc.Get("error.noclient.body"));
            return;
        }

        HideToast();
        _bannerError = null;
        _errorBanner = null;
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
        if (!play)
        {
            target.Overlay = ServerOverlay.None;
            Phase = ServersPhase.Idle;
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

        target.Overlay = ServerOverlay.Running;
        GameClosed = false;
        Phase = ServersPhase.Running;
        ReloadFromService();
        _ = WatchGamePortalAsync(clientId);
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
        _ = WatchGameAsync();
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

        while (Phase == ServersPhase.Running)
        {
            var running = await Task.Run(() => _session.Service.IsGameRunning(clientId)).ConfigureAwait(true);
            if (!running.Succeeded)
            {
                SetError(running.Error);
                return;
            }

            if (!running.Value)
            {
                GameClosed = true;
                Refresh();
                return;
            }

            await Task.Delay(500).ConfigureAwait(true);
        }
    }

    private void Finish()
    {
        if (_target is not null)
        {
            _target.Overlay = ServerOverlay.None;
        }

        _target = null;
        GameClosed = false;
        Phase = ServersPhase.Idle;
        Refresh();
    }

    private void SetError(ServiceError? error, string? message = null)
    {
        _bannerError = message is null ? error : null;
        _errorBanner = message ?? (error is null ? Loc.Get("error.unknown.body") : ErrorText.Body(error));
        OnPropertyChanged(nameof(BannerHeader));
        OnPropertyChanged(nameof(BannerBody));
        OnPropertyChanged(nameof(BannerSeverity));
        OnPropertyChanged(nameof(ShowBanner));
        OnPropertyChanged(nameof(StatusText));
    }

    private void ShowToast()
    {
        _toastElapsed = TimeSpan.Zero;
        ToastFraction = 1;
        ToastVisible = true;
        OnPropertyChanged(nameof(ToastText));
        OnPropertyChanged(nameof(ShowDiscardUndo));
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
        var matches = _all
            .Where(s => query.Length == 0 || s.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase))
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
            item.CanAct = Phase == ServersPhase.Idle;
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
    }
}
