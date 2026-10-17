using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

public enum ServersPhase
{
    Idle,
    Switching,
    Opening,
    Running,
}

/// <summary>
/// The server list: search, switching, undo and play. All of it is simulated by <see cref="DemoBackend"/>.
/// Order of the cards: the server being worked on first, then the active one, then the rest in their saved order.
/// </summary>
public sealed partial class ServersViewModel : ViewModelBase
{
    /// <summary>How long the undo notice stays.</summary>
    public static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(8);

    private static readonly int[] SwitchProgress = [20, 55, 85];
    private static readonly int[] OpenProgress = [25, 60, 100];

    private readonly DemoBackend _backend;
    private readonly List<ServerItemViewModel> _all = [];
    private readonly DispatcherTimer? _toastTimer;
    private ServerItemViewModel? _target;
    private ServerItemViewModel? _undoTo;
    private ServerItemViewModel? _undoFrom;
    private TimeSpan _toastElapsed;

    public ServersViewModel(DemoBackend backend, bool autoTick = true)
    {
        _backend = backend;
        var order = 0;
        foreach (var seed in DemoData.Servers)
        {
            _all.Add(new ServerItemViewModel(seed, item => _ = PlayAsync(item), item => _ = SwitchOnlyAsync(item)) { Order = order++ });
        }

        if (autoTick)
        {
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _toastTimer.Tick += (_, _) => AdvanceToast(_toastTimer.Interval);
        }

        Refresh();
    }

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

    /// <summary>The simulated game was closed: "Reintentar" is enabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerSeverity), nameof(BannerHeader), nameof(BannerBody), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _gameClosed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastText), nameof(StatusText))]
    private bool _toastVisible;

    [ObservableProperty]
    private double _toastFraction = 1;

    public bool IsIdle => Phase == ServersPhase.Idle;

    public bool IsBusy => Phase is ServersPhase.Switching or ServersPhase.Opening;

    public bool IsRunning => Phase == ServersPhase.Running;

    public bool ShowBanner => Phase != ServersPhase.Idle;

    public bool ShowSteps => IsBusy;

    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);

    public bool ShowResults => IsIdle && HasQuery && Visible.Count > 0;

    public bool ShowEmpty => IsIdle && HasQuery && Visible.Count == 0;

    public string ResultsText => Visible.Count == 1
        ? Loc.Format("servers.results.one", SearchText.Trim())
        : Loc.Format("servers.results.many", Visible.Count, SearchText.Trim());

    public string EmptyTitle => Loc.Format("servers.empty.title", SearchText.Trim());

    public BannerSeverity BannerSeverity => Phase switch
    {
        ServersPhase.Running when !GameClosed => BannerSeverity.Warning,
        _ => BannerSeverity.Info,
    };

    public string BannerHeader => Phase switch
    {
        ServersPhase.Switching => Loc.Format("switch.title", _target?.Name),
        ServersPhase.Opening => Loc.Get("open.title"),
        ServersPhase.Running => Loc.Get(GameClosed ? "run.closed.title" : "run.title"),
        _ => string.Empty,
    };

    public string BannerBody => Phase switch
    {
        ServersPhase.Switching => Loc.Get("switch.body"),
        ServersPhase.Opening => Loc.Format("open.body", _target?.Name, _target?.ClientVersion),
        ServersPhase.Running => Loc.Get(GameClosed ? "run.closed.body" : "run.body"),
        _ => string.Empty,
    };

    public string ToastText => _undoTo is null ? string.Empty : Loc.Format("toast.switched", _undoTo.Name);

    public override string StatusText => Phase switch
    {
        ServersPhase.Switching => Loc.Format("status.switching", _target?.Name),
        ServersPhase.Opening => Loc.Get("status.opening"),
        ServersPhase.Running => GameClosed ? Loc.Get("status.closed") : Loc.Format("status.running", _target?.Name),
        _ => ToastVisible && Active is { } active
            ? Loc.Format("status.active", active.Name)
            : Loc.Format("status.realmlist", Active?.Host ?? "-"),
    };

    /// <summary>
    /// What the card list marks as selected: always the active server. The list may try to change it (a click); the change is refused
    /// and the list is told to go back.
    /// </summary>
    public ServerItemViewModel? SelectedCard
    {
        get => Active;
        set => OnPropertyChanged(nameof(SelectedCard));
    }

    /// <summary>Test hook: the server a switch or launch is working on.</summary>
    public ServerItemViewModel? Target => _target;

    /// <summary>Raised when the user asks to add a server (the shell opens the form).</summary>
    public event Action? AddServerRequested;

    [RelayCommand]
    private void RequestAddServer() => AddServerRequested?.Invoke();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand(CanExecute = nameof(GameClosed))]
    private void Retry()
    {
        Finish();
    }

    [RelayCommand]
    private void Undo()
    {
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
    private void DismissToast() => HideToast();

    /// <summary>Moves the toast countdown forward (the timer calls it every 100 ms; tests call it directly).</summary>
    public void AdvanceToast(TimeSpan elapsed)
    {
        if (!ToastVisible)
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

    /// <summary>Adds a server to the in-memory list (the add-server form).</summary>
    public ServerItemViewModel AddServer(ServerSeed seed)
    {
        var item = new ServerItemViewModel(seed, i => _ = PlayAsync(i), i => _ = SwitchOnlyAsync(i)) { Order = _all.Count == 0 ? 0 : _all.Max(s => s.Order) + 1 };
        _all.Add(item);
        SearchText = string.Empty;
        Refresh();
        return item;
    }

    public Task SwitchOnlyAsync(ServerItemViewModel target) =>
        RunAsync(target, ServersPhase.Switching, [StepKind.Copy, StepKind.Realmlist, StepKind.Account], SwitchProgress);

    public Task PlayAsync(ServerItemViewModel target) =>
        RunAsync(target, ServersPhase.Opening, [StepKind.Realmlist, StepKind.Account, StepKind.Launch], OpenProgress);

    private async Task RunAsync(ServerItemViewModel target, ServersPhase phase, StepKind[] kinds, int[] progress)
    {
        if (Phase != ServersPhase.Idle || !target.CanPlay)
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
        await _backend.GameExited.ConfigureAwait(true);
        if (Phase == ServersPhase.Running)
        {
            GameClosed = true;
            Refresh();
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

    private void ShowToast()
    {
        _toastElapsed = TimeSpan.Zero;
        ToastFraction = 1;
        ToastVisible = true;
        OnPropertyChanged(nameof(ToastText));
        _toastTimer?.Start();
    }

    private void HideToast()
    {
        _toastTimer?.Stop();
        ToastVisible = false;
        _undoFrom = null;
        _undoTo = null;
        OnPropertyChanged(nameof(ToastText));
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

    /// <summary>Rebuilds the visible cards and the state-dependent flags.</summary>
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
    }
}
