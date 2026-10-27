using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core;
using RealmPortal.Core.Service;
using AppStepState = RealmPortal.App.Controls.StepState;

namespace RealmPortal.App.ViewModels;

/// <summary>One-time first-run welcome: language → clients → servers.</summary>
public sealed partial class WelcomeViewModel : ViewModelBase
{
    public enum WelcomeStep
    {
        Language = 0,
        Clients = 1,
        Servers = 2,
    }

    private readonly PortalSession _session;
    private readonly ClientsViewModel _clients;
    private readonly ServersViewModel _servers;
    private readonly SettingsViewModel _settings;
    private readonly Action _onFinished;
    private readonly Func<Task>? _openAddServer;
    private bool _clientsScanStarted;

    public WelcomeViewModel(
        PortalSession session,
        ClientsViewModel clients,
        ServersViewModel servers,
        SettingsViewModel settings,
        Action onFinished,
        Func<Task>? openAddServer = null)
    {
        _session = session;
        _clients = clients;
        _servers = servers;
        _settings = settings;
        _onFinished = onFinished;
        _openAddServer = openAddServer;

        LanguageStep = new WelcomeProgressStep(1, "welcome.step.language", AppStepState.Current, isLast: false);
        ClientsStep = new WelcomeProgressStep(2, "welcome.step.clients", AppStepState.Pending, isLast: false);
        ServersStep = new WelcomeProgressStep(3, "welcome.step.servers", AppStepState.Pending, isLast: true);
        ProgressSteps = [LanguageStep, ClientsStep, ServersStep];

        // Seed the picker from the live UI language (system default on a fresh install).
        _isSpanish = Loc.Instance.Current == Language.Es;
        _isEnglish = !_isSpanish;

        _clients.ClientsChanged += OnClientsChanged;
        _clients.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClientsViewModel.IsScanning)
                or nameof(ClientsViewModel.Summary)
                or nameof(ClientsViewModel.Notice)
                or nameof(ClientsViewModel.HasNotice)
                or nameof(ClientsViewModel.StatusText))
            {
                OnPropertyChanged(nameof(ClientsSummary));
                OnPropertyChanged(nameof(IsScanningClients));
                OnPropertyChanged(nameof(HasClientNotice));
                OnPropertyChanged(nameof(ClientNotice));
                OnPropertyChanged(nameof(HasClients));
                OnPropertyChanged(nameof(NoClients));
                OnPropertyChanged(nameof(ContinueClientsLabel));
                OnPropertyChanged(nameof(StatusText));
                ContinueClientsCommand.NotifyCanExecuteChanged();
                RescanClientsCommand.NotifyCanExecuteChanged();
                PickClientFolderCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public ObservableCollection<WelcomeProgressStep> ProgressSteps { get; }

    public WelcomeProgressStep LanguageStep { get; }

    public WelcomeProgressStep ClientsStep { get; }

    public WelcomeProgressStep ServersStep { get; }

    public ClientsViewModel Clients => _clients;

    public ObservableCollection<ServerItemViewModel> ServerItems => _servers.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsLanguageStep),
        nameof(IsClientsStep),
        nameof(IsServersStep),
        nameof(Title),
        nameof(Subtitle),
        nameof(StatusText),
        nameof(ContinueClientsLabel))]
    private WelcomeStep _step = WelcomeStep.Language;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnglish))]
    private bool _isSpanish;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpanish))]
    private bool _isEnglish;

    public bool IsLanguageStep => Step == WelcomeStep.Language;

    public bool IsClientsStep => Step == WelcomeStep.Clients;

    public bool IsServersStep => Step == WelcomeStep.Servers;

    public bool IsScanningClients => _clients.IsScanning;

    public bool HasClients => _clients.Items.Count > 0;

    public bool NoClients => !IsScanningClients && _clients.Items.Count == 0;

    public bool HasServers => ServerItems.Count > 0;

    public bool HasClientNotice => _clients.HasNotice;

    public string? ClientNotice => _clients.Notice;

    public string ClientsSummary => _clients.Summary;

    public string Title => Step switch
    {
        WelcomeStep.Language => Loc.Get("welcome.language.title"),
        WelcomeStep.Clients when IsScanningClients => Loc.Get("welcome.clients.searching"),
        WelcomeStep.Clients when NoClients => Loc.Get("welcome.clients.none.title"),
        WelcomeStep.Clients => _clients.Items.Count == 1
            ? Loc.Get("welcome.clients.found.one")
            : Loc.Format("welcome.clients.found.many", _clients.Items.Count),
        WelcomeStep.Servers when ServerItems.Count == 0 => Loc.Get("welcome.servers.empty.title"),
        WelcomeStep.Servers when ServerItems.Count == 1 => Loc.Get("welcome.servers.found.one"),
        WelcomeStep.Servers => Loc.Format("welcome.servers.found.many", ServerItems.Count),
        _ => Loc.Get("welcome.language.title"),
    };

    public string Subtitle => Step switch
    {
        WelcomeStep.Language => Loc.Get("welcome.language.subtitle"),
        WelcomeStep.Clients when IsScanningClients => Loc.Get("welcome.clients.searching.hint"),
        WelcomeStep.Clients when NoClients => Loc.Get("welcome.clients.none.body"),
        WelcomeStep.Clients => Loc.Get("welcome.clients.hint"),
        WelcomeStep.Servers when ServerItems.Count == 0 => Loc.Get("welcome.servers.empty.hint"),
        WelcomeStep.Servers => Loc.Get("welcome.servers.hint"),
        _ => string.Empty,
    };

    public string ContinueClientsLabel =>
        _clients.InUseCount <= 0
            ? Loc.Get("welcome.clients.continue.empty")
            : Loc.Format("welcome.clients.continue", _clients.InUseCount);

    public override string StatusText => Step switch
    {
        WelcomeStep.Language => Loc.Get("welcome.status.language"),
        WelcomeStep.Clients when IsScanningClients => Loc.Get("status.scanning"),
        WelcomeStep.Clients when NoClients => Loc.Get("welcome.status.noclients"),
        WelcomeStep.Clients => Loc.Get("welcome.status.clients"),
        WelcomeStep.Servers => Loc.Get("welcome.status.servers"),
        _ => Loc.Get("welcome.status.language"),
    };

    public override void RefreshText()
    {
        foreach (var step in ProgressSteps)
            step.RefreshText();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ContinueClientsLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ClientsSummary));
        OnPropertyChanged(nameof(LanguageFootnote));
        OnPropertyChanged(nameof(SkipLabel));
        OnPropertyChanged(nameof(SkipHint));
        OnPropertyChanged(nameof(ContinueLabel));
        OnPropertyChanged(nameof(FinishLabel));
        OnPropertyChanged(nameof(SpanishTitle));
        OnPropertyChanged(nameof(SpanishBody));
        OnPropertyChanged(nameof(EnglishTitle));
        OnPropertyChanged(nameof(EnglishBody));
        OnPropertyChanged(nameof(LanguageLabel));
    }

    public string LanguageFootnote => Loc.Get("welcome.language.footnote");

    public string SkipLabel => Loc.Get("welcome.skip");

    public string SkipHint => Loc.Get("welcome.skip.hint");

    public string ContinueLabel => Loc.Get("welcome.continue");

    public string FinishLabel => Loc.Get("welcome.finish");

    public string LanguageLabel => $"{Loc.Instance.Code} · v{AppInfo.Version}";

    public string SpanishTitle => Loc.Get("welcome.language.es.title");

    public string SpanishBody => Loc.Get("welcome.language.es.body");

    public string EnglishTitle => Loc.Get("welcome.language.en.title");

    public string EnglishBody => Loc.Get("welcome.language.en.body");

    partial void OnIsSpanishChanged(bool value)
    {
        if (value)
        {
            IsEnglish = false;
            ApplyLanguage(Language.Es);
        }
    }

    partial void OnIsEnglishChanged(bool value)
    {
        if (value)
        {
            IsSpanish = false;
            ApplyLanguage(Language.En);
        }
    }

    private void ApplyLanguage(Language language)
    {
        if (Loc.Instance.Current != language)
            Loc.Instance.SetLanguage(language);
        _session.UiPreferences.SetLanguage(language);
        _settings.AcceptSavedLanguage(language);
    }

    private void OnClientsChanged()
    {
        OnPropertyChanged(nameof(HasClients));
        OnPropertyChanged(nameof(NoClients));
        OnPropertyChanged(nameof(ClientsSummary));
        OnPropertyChanged(nameof(ContinueClientsLabel));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        ContinueClientsCommand.NotifyCanExecuteChanged();
    }

    partial void OnStepChanged(WelcomeStep value)
    {
        UpdateProgress();
        if (value == WelcomeStep.Clients)
            StartClientsScanIfNeeded();
        if (value == WelcomeStep.Servers)
        {
            _servers.ReloadFromService();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(HasServers));
        }
    }

    private void UpdateProgress()
    {
        LanguageStep.State = Step >= WelcomeStep.Clients ? AppStepState.Done : AppStepState.Current;
        ClientsStep.State = Step switch
        {
            WelcomeStep.Language => AppStepState.Pending,
            WelcomeStep.Clients => AppStepState.Current,
            _ => AppStepState.Done,
        };
        ServersStep.State = Step == WelcomeStep.Servers ? AppStepState.Current : AppStepState.Pending;
    }

    private void StartClientsScanIfNeeded()
    {
        if (_clientsScanStarted)
            return;
        _clientsScanStarted = true;
        // Drive the same scan path as the Clients screen; do not jump the sidebar away mid-welcome.
        _clients.FirstScanFoundNoClients -= IgnoreEmptyScanNavigation;
        _clients.FirstScanFoundNoClients += IgnoreEmptyScanNavigation;
        if (!_session.UiPreferences.Load().FirstScanCompleted)
            _ = RescanClientsAsync();
        else
            _clients.ReloadFromService();
    }

    private void IgnoreEmptyScanNavigation()
    {
        // Swallow: MainViewModel also listens; welcome keeps the user on this step.
    }

    [RelayCommand]
    private void SelectSpanish()
    {
        IsSpanish = true;
    }

    [RelayCommand]
    private void SelectEnglish()
    {
        IsEnglish = true;
    }

    [RelayCommand]
    private void ContinueLanguage()
    {
        if (!IsSpanish && !IsEnglish)
            IsSpanish = true;
        ApplyLanguage(IsSpanish ? Language.Es : Language.En);
        Step = WelcomeStep.Clients;
    }

    [RelayCommand(CanExecute = nameof(CanContinueClients))]
    private void ContinueClients() => Step = WelcomeStep.Servers;

    private bool CanContinueClients() => !IsScanningClients;

    [RelayCommand(CanExecute = nameof(CanScanClients))]
    private async Task RescanClientsAsync()
    {
        if (_clients.RescanCommand.CanExecute(null))
            await _clients.RescanCommand.ExecuteAsync(null).ConfigureAwait(true);
        OnClientsChanged();
        OnPropertyChanged(nameof(IsScanningClients));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(StatusText));
        ContinueClientsCommand.NotifyCanExecuteChanged();
        RescanClientsCommand.NotifyCanExecuteChanged();
        PickClientFolderCommand.NotifyCanExecuteChanged();
    }

    private bool CanScanClients() => !IsScanningClients;

    [RelayCommand(CanExecute = nameof(CanScanClients))]
    private async Task PickClientFolderAsync()
    {
        if (_clients.PickFolderCommand.CanExecute(null))
            await _clients.PickFolderCommand.ExecuteAsync(null).ConfigureAwait(true);
        OnClientsChanged();
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        CompleteWelcome();
        if (_openAddServer is not null)
            await _openAddServer().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Finish() => CompleteWelcome();

    [RelayCommand]
    private void Skip() => CompleteWelcome();

    private void CompleteWelcome()
    {
        var prefs = _session.UiPreferences.Load();
        _session.UiPreferences.Save(prefs with { WelcomeCompleted = true });
        _clients.FirstScanFoundNoClients -= IgnoreEmptyScanNavigation;
        _onFinished();
    }
}

/// <summary>One chip in the welcome progress strip.</summary>
public sealed partial class WelcomeProgressStep : ObservableObject
{
    private readonly string _labelKey;

    public WelcomeProgressStep(int number, string labelKey, AppStepState state, bool isLast)
    {
        Number = number;
        _labelKey = labelKey;
        _state = state;
        SlotWidth = isLast ? 120 : 160;
        ShowConnector = !isLast;
    }

    public int Number { get; }

    public double SlotWidth { get; }

    public bool ShowConnector { get; }

    [ObservableProperty]
    private AppStepState _state;

    public string Label => Loc.Format("step.label", Number, Loc.Get(_labelKey));

    public void RefreshText() => OnPropertyChanged(nameof(Label));
}
