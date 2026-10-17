using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

/// <summary>What a card shows on top of its normal status while the app is busy with it.</summary>
public enum ServerOverlay
{
    None,
    Switching,
    Opening,
    Running,
}

/// <summary>One server card.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    private readonly Action<ServerItemViewModel> _play;
    private readonly Action<ServerItemViewModel> _switchOnly;

    public ServerItemViewModel(ServerSeed seed, Action<ServerItemViewModel> play, Action<ServerItemViewModel> switchOnly)
    {
        Name = seed.Name;
        Host = seed.Host;
        Address = $"{seed.Host}:{seed.Port}";
        ClientVersion = seed.ClientVersion;
        Account = seed.Account;
        IsCompatible = seed.IsCompatible;
        IsActive = seed.IsActive;
        _play = play;
        _switchOnly = switchOnly;
    }

    public string Name { get; }

    public string Host { get; }

    /// <summary>host:port.</summary>
    public string Address { get; }

    public string ClientVersion { get; }

    public string? Account { get; }

    public bool IsCompatible { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SealStatus), nameof(BadgeStatus), nameof(BadgeText))]
    private bool _isActive;

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSwitchOnly));
        OnPropertyChanged(nameof(SwitchOnlyHint));
        SwitchOnlyCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeStatus), nameof(BadgeText))]
    private ServerOverlay _overlay;

    [ObservableProperty]
    private bool _canAct = true;

    /// <summary>Position in the unfiltered list: keeps the order of the idle servers stable.</summary>
    public int Order { get; init; }

    public ServerStatus SealStatus => IsActive ? ServerStatus.Active : IsCompatible ? ServerStatus.Ready : ServerStatus.Incompatible;

    public ServerStatus BadgeStatus => Overlay switch
    {
        ServerOverlay.Switching or ServerOverlay.Opening => ServerStatus.Inactive,
        ServerOverlay.Running => ServerStatus.Active,
        _ => SealStatus,
    };

    public string BadgeText => Overlay switch
    {
        ServerOverlay.Switching => Loc.Get("badge.switching"),
        ServerOverlay.Opening => Loc.Get("badge.opening"),
        ServerOverlay.Running => Loc.Get("badge.running"),
        _ => SealStatus switch
        {
            ServerStatus.Active => Loc.Get("badge.active"),
            ServerStatus.Incompatible => Loc.Get("badge.incompatible"),
            _ => Loc.Get("badge.ready"),
        },
    };

    /// <summary>"Cliente 3.3.5a (12340) · Cuenta: demo".</summary>
    public string DetailText => Account is null
        ? Loc.Format("card.detail.noaccount", ClientVersion)
        : Loc.Format("card.detail.account", ClientVersion, Account);

    /// <summary>Lower-case text the search box matches against.</summary>
    public string SearchText => $"{Name} {Address} {ClientVersion} {Account}";

    public bool CanPlay => CanAct && IsCompatible;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => _play(this);

    /// <summary>"Switch only" makes no sense for the server that is already active.</summary>
    public bool CanSwitchOnly => CanPlay && !IsActive;

    /// <summary>The line under "Switch only" in the play menu: what it does, or why it is unavailable.</summary>
    public string SwitchOnlyHint => Loc.Get(IsActive ? "menu.switchonly.active" : "menu.switchonly.hint");

    [RelayCommand(CanExecute = nameof(CanSwitchOnly))]
    private void SwitchOnly() => _switchOnly(this);

    partial void OnCanActChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanSwitchOnly));
        PlayCommand.NotifyCanExecuteChanged();
        SwitchOnlyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-reads every translated text (after a language change).</summary>
    public void RefreshText()
    {
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(SwitchOnlyHint));
    }
}
