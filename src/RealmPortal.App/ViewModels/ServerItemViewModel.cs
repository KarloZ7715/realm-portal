using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.Core.Network;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Servers;
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

/// <summary>Why a server card's Play/Switch/Edit/Delete actions are unavailable.</summary>
public enum ServerCardLock
{
    None,
    ClientRunning,
    NoInUseClient,
}

/// <summary>One server card.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    private readonly Func<string> _clientVersion;
    private Func<string> _clientTitle;
    private readonly Action<ServerItemViewModel> _play;
    private readonly Action<ServerItemViewModel> _switchOnly;
    private readonly Action<ServerItemViewModel>? _edit;
    private readonly Action<ServerItemViewModel>? _delete;

    private ServerItemViewModel(
        string? serverId,
        string? preferredClientId,
        string name,
        string host,
        string address,
        Func<string> clientVersion,
        string? account,
        bool isCompatible,
        bool isActive,
        bool needsRepair,
        Action<ServerItemViewModel> play,
        Action<ServerItemViewModel> switchOnly,
        Action<ServerItemViewModel>? edit,
        Action<ServerItemViewModel>? delete,
        bool showClientLabel,
        string? clientDisplayName,
        string? effectiveClientId,
        IReadOnlyCollection<string>? compatibleClientIds)
    {
        ServerId = serverId;
        PreferredClientId = preferredClientId;
        Name = name;
        Host = host;
        Address = address;
        _clientVersion = clientVersion;
        _clientTitle = () => Loc.Format("clients.client", clientVersion());
        Account = account;
        IsCompatible = isCompatible;
        IsActive = isActive;
        NeedsRepair = needsRepair;
        _play = play;
        _switchOnly = switchOnly;
        _edit = edit;
        _delete = delete;
        ShowClientLabel = showClientLabel;
        ClientDisplayName = clientDisplayName;
        EffectiveClientId = effectiveClientId;
        CompatibleClientIds = compatibleClientIds ?? [];
    }

    public static ServerItemViewModel FromSeed(
        ServerSeed seed,
        Action<ServerItemViewModel> play,
        Action<ServerItemViewModel> switchOnly,
        Action<ServerItemViewModel>? edit = null,
        Action<ServerItemViewModel>? delete = null) =>
        new(
            null,
            null,
            seed.Name,
            seed.Host,
            $"{seed.Host}:{seed.Port}",
            () => seed.ClientVersion,
            seed.Account,
            seed.IsCompatible,
            seed.IsActive,
            false,
            play,
            switchOnly,
            edit,
            delete,
            false,
            null,
            null,
            null);

    public static ServerItemViewModel FromEntry(
        ServerEntry entry,
        ClientVersion? clientVersion,
        bool projectionInSync,
        Action<ServerItemViewModel> play,
        Action<ServerItemViewModel> switchOnly,
        string? effectiveClientId = null,
        IReadOnlyCollection<string>? compatibleClientIds = null,
        bool showClientLabel = false,
        string? clientDisplayName = null,
        Action<ServerItemViewModel>? edit = null,
        Action<ServerItemViewModel>? delete = null)
    {
        var needsRepair = entry.IsActive && !projectionInSync;
        // An unreadable or missing client matches the service: every server stays compatible.
        // A known version uses the same build rule as the client RunPortalAsync will switch with.
        var compatible = clientVersion is null || CompatibilityFilter.IsCompatible(entry, clientVersion);
        var item = new ServerItemViewModel(
            entry.Id,
            entry.ClientId,
            entry.Name,
            entry.Host,
            entry.Host,
            () => VersionText.Of(clientVersion),
            entry.Account,
            compatible,
            entry.IsActive,
            needsRepair,
            play,
            switchOnly,
            edit,
            delete,
            showClientLabel,
            clientDisplayName,
            effectiveClientId,
            compatibleClientIds);
        item._clientTitle = () => VersionText.ClientTitle(clientVersion);
        return item;
    }

    public string? ServerId { get; }

    public string? PreferredClientId { get; }

    /// <summary>The client this card would launch with right now (pinned, else the resolved in-use client).</summary>
    public string? EffectiveClientId { get; }

    /// <summary>Ids of the saved clients this server is compatible with (used by the header client filter).</summary>
    public IReadOnlyCollection<string> CompatibleClientIds { get; }

    public string Name { get; }

    public string Host { get; }

    /// <summary>host:port or host from the service.</summary>
    public string Address { get; }

    /// <summary>The client version text; re-read on language changes because an unreadable version is localized.</summary>
    public string ClientVersion => _clientVersion();

    public string? Account { get; }

    public bool IsCompatible { get; }

    /// <summary>Active server whose client files or projection are out of sync; re-switch runs a repair.</summary>
    public bool NeedsRepair { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SealStatus), nameof(BadgeStatus), nameof(BadgeText), nameof(ShowSwitchMenu), nameof(SwitchMenuText))]
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

    /// <summary>"Cliente 3.3.5a (12340) · Cuenta: demo", or "Versión de cliente desconocida · Sin cuenta".</summary>
    public string DetailText => Account is null
        ? Loc.Format("card.detail.noaccount", _clientTitle())
        : Loc.Format("card.detail.account", _clientTitle(), Account);

    /// <summary>Lower-case text the search box matches against.</summary>
    public string SearchText => $"{Name} {Address} {ClientVersion} {Account}";

    public bool CanPlay => CanAct && IsCompatible;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => _play(this);

    /// <summary>"Switch only" is disabled for the active server, unless its projection is out of sync and a repair can run.</summary>
    public bool CanSwitchOnly => CanPlay && (!IsActive || NeedsRepair);

    /// <summary>The line under "Switch only" in the play menu: what it does, or why it is unavailable.</summary>
    public string SwitchOnlyHint => Loc.Get(IsActive && !NeedsRepair ? "menu.switchonly.active" : "menu.switchonly.hint");

    [RelayCommand(CanExecute = nameof(CanSwitchOnly))]
    private void SwitchOnly() => _switchOnly(this);

    /// <summary>True when the card has an edit/delete action wired (production list only).</summary>
    public bool CanManage => _edit is not null || _delete is not null;

    /// <summary>Edit is disabled while the app is busy or this card's client is running a game.</summary>
    public bool CanEdit => _edit is not null && CanAct;

    public bool CanDelete => _delete is not null && CanAct;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit() => _edit?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete() => _delete?.Invoke(this);

    private ServerCardLock _lock;
    private string? _lockClientName;

    /// <summary>Why Play/Switch/Edit/Delete are locked on this card. None means the menu is fully usable.</summary>
    public ServerCardLock Lock => _lock;

    /// <summary>Resolved target client display name, shown on the card when more than one client is saved.</summary>
    public string? ClientDisplayName { get; }

    public bool ShowClientLabel { get; }

    /// <summary>"Client: WotLK" line appended to the card metadata when there is more than one client.</summary>
    public string ClientLabel => ClientDisplayName is null
        ? string.Empty
        : Loc.Format("servers.card.client", ClientDisplayName);

    /// <summary>The disabled-reason caption under the card menu, or null when the menu is fully usable.</summary>
    public string? MenuDisabledHint => _lock switch
    {
        ServerCardLock.ClientRunning => _lockClientName is null
            ? null
            : Loc.Format("menu.disabled.clientrunning", _lockClientName),
        ServerCardLock.NoInUseClient => Loc.Get("menu.disabled.noinuse"),
        _ => null,
    };

    public bool ShowMenuDisabledHint => !string.IsNullOrEmpty(MenuDisabledHint);

    /// <summary>Sets the per-card lock reason (recomputed on every list refresh).</summary>
    public void SetLock(ServerCardLock reason, string? clientName)
    {
        _lock = reason;
        _lockClientName = clientName;
        OnPropertyChanged(nameof(Lock));
        OnPropertyChanged(nameof(MenuDisabledHint));
        OnPropertyChanged(nameof(ShowMenuDisabledHint));
    }

    public void SetNeedsRepair(bool needsRepair)
    {
        NeedsRepair = needsRepair;
        OnPropertyChanged(nameof(NeedsRepair));
        OnPropertyChanged(nameof(ShowSwitchMenu));
        OnPropertyChanged(nameof(SwitchMenuText));
        OnPropertyChanged(nameof(SwitchOnlyHint));
        OnPropertyChanged(nameof(CanSwitchOnly));
        OnPropertyChanged(nameof(BadgeText));
        SwitchOnlyCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanActChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanSwitchOnly));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        PlayCommand.NotifyCanExecuteChanged();
        SwitchOnlyCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-reads every translated text (after a language change).</summary>
    public void RefreshText()
    {
        OnPropertyChanged(nameof(ClientVersion));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(SwitchMenuText));
        OnPropertyChanged(nameof(SwitchOnlyHint));
        OnPropertyChanged(nameof(ClientLabel));
        OnPropertyChanged(nameof(MenuDisabledHint));
        OnPropertyChanged(nameof(ShowMenuDisabledHint));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(StatusAccessibleName));
    }

    // --- Login reachability status dot (#104) ---

    private ServerProbeStatus _loginStatus = ServerProbeStatus.Unknown;
    private string? _statusReason;
    private DateTimeOffset? _statusCheckedAt;
    private bool _showStatusDot;

    /// <summary>When false the status dot is hidden (probes disabled in Settings).</summary>
    public bool ShowStatusDot
    {
        get => _showStatusDot;
        private set
        {
            if (SetProperty(ref _showStatusDot, value))
                NotifyStatusDot();
        }
    }

    public ServerProbeStatus LoginStatus => _loginStatus;

    public bool IsStatusOnline => _showStatusDot && _loginStatus == ServerProbeStatus.Online;
    public bool IsStatusOffline => _showStatusDot && _loginStatus == ServerProbeStatus.Offline;
    public bool IsStatusNeutral => _showStatusDot
        && (_loginStatus is ServerProbeStatus.Unknown or ServerProbeStatus.Checking);

    public string StatusTooltip => BuildStatusTooltip();

    public string StatusAccessibleName => StatusTooltip;

    public void SetStatusDotVisible(bool visible) => ShowStatusDot = visible;

    public void ApplyProbeSnapshot(ServerProbeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _loginStatus = snapshot.Status;
        _statusReason = snapshot.Reason;
        _statusCheckedAt = snapshot.CheckedAtUtc;
        NotifyStatusDot();
    }

    public void ClearProbeStatus()
    {
        _loginStatus = ServerProbeStatus.Unknown;
        _statusReason = null;
        _statusCheckedAt = null;
        NotifyStatusDot();
    }

    private void NotifyStatusDot()
    {
        OnPropertyChanged(nameof(LoginStatus));
        OnPropertyChanged(nameof(IsStatusOnline));
        OnPropertyChanged(nameof(IsStatusOffline));
        OnPropertyChanged(nameof(IsStatusNeutral));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(StatusAccessibleName));
        OnPropertyChanged(nameof(ShowStatusDot));
    }

    private string BuildStatusTooltip()
    {
        var age = FormatAge(_statusCheckedAt);
        var reason = ResolveReason(_statusReason);
        return _loginStatus switch
        {
            ServerProbeStatus.Online when age is not null =>
                Loc.Format("status.probe.online.age", age),
            ServerProbeStatus.Online =>
                Loc.Get("status.probe.online"),
            ServerProbeStatus.Offline when reason is { Length: > 0 } && age is not null =>
                Loc.Format("status.probe.offline.age", reason, age),
            ServerProbeStatus.Offline when reason is { Length: > 0 } =>
                Loc.Format("status.probe.offline.reason", reason),
            ServerProbeStatus.Offline =>
                Loc.Get("status.probe.offline"),
            ServerProbeStatus.Checking =>
                Loc.Get("status.probe.checking"),
            _ when reason is { Length: > 0 } =>
                Loc.Format("status.probe.unknown.reason", reason),
            _ =>
                Loc.Get("status.probe.unknown"),
        };
    }

    /// <summary>Maps Core English probe reasons (and optional Loc keys) to the UI language.</summary>
    private static string? ResolveReason(string? reasonOrKey)
    {
        if (string.IsNullOrEmpty(reasonOrKey))
            return null;
        if (reasonOrKey.StartsWith("status.probe.reason.", StringComparison.Ordinal))
            return Loc.Get(reasonOrKey);
        // Core ProbeScheduler.Reason* constants (English).
        return reasonOrKey switch
        {
            ProbeScheduler.ReasonTimedOut => Loc.Get("status.probe.reason.timeout"),
            ProbeScheduler.ReasonDns => Loc.Get("status.probe.reason.dns"),
            ProbeScheduler.ReasonConnectionRefused => Loc.Get("status.probe.reason.refused"),
            ProbeScheduler.ReasonLoginReachable => Loc.Get("status.probe.online"),
            "invalid realmlist host" => Loc.Get("status.probe.reason.invalidhost"),
            "connection failed" or "offline" or "network error" => Loc.Get("status.probe.reason.failed"),
            _ when reasonOrKey.StartsWith("network error", StringComparison.Ordinal) =>
                Loc.Get("status.probe.reason.failed"),
            _ => reasonOrKey,
        };
    }

    private static string? FormatAge(DateTimeOffset? checkedAt)
    {
        if (checkedAt is null)
            return null;
        var elapsed = DateTimeOffset.UtcNow - checkedAt.Value;
        if (elapsed < TimeSpan.FromSeconds(45))
            return Loc.Get("status.probe.age.justnow");
        if (elapsed < TimeSpan.FromHours(1))
            return Loc.Format("status.probe.age.minutes", Math.Max(1, (int)elapsed.TotalMinutes));
        return Loc.Format("status.probe.age.hours", Math.Max(1, (int)elapsed.TotalHours));
    }
}
