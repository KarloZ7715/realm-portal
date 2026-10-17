using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Service;

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

    private ServerItemViewModel(
        string? serverId,
        string? preferredClientId,
        string name,
        string host,
        string address,
        string clientVersion,
        string? account,
        bool isCompatible,
        bool isActive,
        bool needsRepair,
        Action<ServerItemViewModel> play,
        Action<ServerItemViewModel> switchOnly)
    {
        ServerId = serverId;
        PreferredClientId = preferredClientId;
        Name = name;
        Host = host;
        Address = address;
        ClientVersion = clientVersion;
        Account = account;
        IsCompatible = isCompatible;
        IsActive = isActive;
        NeedsRepair = needsRepair;
        _play = play;
        _switchOnly = switchOnly;
    }

    public static ServerItemViewModel FromSeed(
        ServerSeed seed,
        Action<ServerItemViewModel> play,
        Action<ServerItemViewModel> switchOnly) =>
        new(
            null,
            null,
            seed.Name,
            seed.Host,
            $"{seed.Host}:{seed.Port}",
            seed.ClientVersion,
            seed.Account,
            seed.IsCompatible,
            seed.IsActive,
            false,
            play,
            switchOnly);

    public static ServerItemViewModel FromEntry(
        ServerEntry entry,
        ClientVersion? clientVersion,
        bool projectionInSync,
        Action<ServerItemViewModel> play,
        Action<ServerItemViewModel> switchOnly)
    {
        var versionLabel = clientVersion?.ToString() ?? "?";
        var needsRepair = entry.IsActive && !projectionInSync;
        return new(
            entry.Id,
            entry.ClientId,
            entry.Name,
            entry.Host,
            entry.Host,
            versionLabel,
            entry.Account,
            entry.IsCompatible,
            entry.IsActive,
            needsRepair,
            play,
            switchOnly);
    }

    public string? ServerId { get; }

    public string? PreferredClientId { get; }

    public string Name { get; }

    public string Host { get; }

    /// <summary>host:port or host from the service.</summary>
    public string Address { get; }

    public string ClientVersion { get; }

    public string? Account { get; }

    public bool IsCompatible { get; }

    /// <summary>Active server whose client files or projection are out of sync; re-switch runs a repair.</summary>
    public bool NeedsRepair { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SealStatus), nameof(BadgeStatus), nameof(BadgeText), nameof(ShowSwitchMenu), nameof(SwitchMenuText))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeStatus), nameof(BadgeText))]
    private ServerOverlay _overlay;

    [ObservableProperty]
    private bool _canAct = true;

    /// <summary>Position in the unfiltered list: keeps the order of the idle servers stable.</summary>
    public int Order { get; set; }

    public bool ShowSwitchMenu => !IsActive || NeedsRepair;

    public string SwitchMenuText => NeedsRepair ? Loc.Get("card.repair") : Loc.Get("card.switchonly");

    public ServerStatus SealStatus => IsActive ? ServerStatus.Active : IsCompatible ? ServerStatus.Ready : ServerStatus.Incompatible;

    public ServerStatus BadgeStatus => Overlay switch
    {
        ServerOverlay.Switching or ServerOverlay.Opening => ServerStatus.Inactive,
        ServerOverlay.Running => ServerStatus.Active,
        _ => SealStatus,
    };

    public string BadgeText => Overlay switch
    {
        ServerOverlay.Switching => Loc.Get(NeedsRepair ? "badge.repairing" : "badge.switching"),
        ServerOverlay.Opening => Loc.Get("badge.opening"),
        ServerOverlay.Running => Loc.Get("badge.running"),
        _ => SealStatus switch
        {
            ServerStatus.Active when NeedsRepair => Loc.Get("badge.needsrepair"),
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

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void SwitchOnly() => _switchOnly(this);

    public void SetNeedsRepair(bool needsRepair)
    {
        NeedsRepair = needsRepair;
        OnPropertyChanged(nameof(NeedsRepair));
        OnPropertyChanged(nameof(ShowSwitchMenu));
        OnPropertyChanged(nameof(SwitchMenuText));
        OnPropertyChanged(nameof(BadgeText));
    }

    partial void OnCanActChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        PlayCommand.NotifyCanExecuteChanged();
        SwitchOnlyCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSwitchMenu));
        OnPropertyChanged(nameof(SwitchMenuText));
    }

    /// <summary>Re-reads every translated text (after a language change).</summary>
    public void RefreshText()
    {
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(SwitchMenuText));
    }
}
