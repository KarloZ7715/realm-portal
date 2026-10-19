using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.Core.Service;
using CoreClientOrigin = RealmPortal.Core.Clients.ClientOrigin;

namespace RealmPortal.App.ViewModels;

/// <summary>One detected (or hand-picked) game client row.</summary>
public sealed partial class ClientItemViewModel : ObservableObject
{
    private readonly Action<ClientItemViewModel>? _edit;
    private readonly Action<ClientItemViewModel>? _remove;

    private ClientItemViewModel(
        string? id,
        string? name,
        string version,
        string path,
        CoreClientOrigin origin,
        bool inUse,
        bool isRunning,
        bool isStateUnknown,
        Action<ClientItemViewModel>? edit,
        Action<ClientItemViewModel>? remove)
    {
        Id = id;
        Name = name ?? string.Empty;
        Version = version;
        Path = path;
        CoreOrigin = origin;
        Origin = Enum.TryParse<ClientOrigin>(origin.ToString(), true, out var demoOrigin)
            ? demoOrigin : ClientOrigin.Manual;
        _isInUse = inUse;
        _isRunning = isRunning;
        _isStateUnknown = isStateUnknown;
        _edit = edit;
        _remove = remove;
    }

    public ClientItemViewModel(ClientSeed seed, Action<ClientItemViewModel>? edit = null, Action<ClientItemViewModel>? remove = null)
        : this(null, null, seed.Version, seed.Path, Enum.Parse<CoreClientOrigin>(seed.Origin.ToString()), seed.InUse, false, false, edit, remove)
    {
    }

    public static ClientItemViewModel FromEntry(
        ClientEntry entry,
        bool inUse,
        bool isRunning = false,
        bool isStateUnknown = false,
        Action<ClientItemViewModel>? edit = null,
        Action<ClientItemViewModel>? remove = null)
    {
        var version = entry.Version?.ToString() ?? entry.Error?.Code.ToString() ?? "?";
        return new ClientItemViewModel(entry.Id, entry.Name, version, entry.Path, entry.Origin, inUse,
            isRunning, isStateUnknown, edit, remove);
    }

    public string? Id { get; }

    public string Name { get; private set; }

    public string Version { get; }

    public string Path { get; private set; }

    // Keep the demo-facing enum for existing view model consumers; CoreOrigin retains
    // the full service value set, including Discovered.
    public ClientOrigin Origin { get; }

    public CoreClientOrigin CoreOrigin { get; }

    public string OriginText => Loc.Get($"clients.origin.{CoreOrigin.ToString().ToLowerInvariant()}");

    public string Title => string.IsNullOrWhiteSpace(Name) ? Loc.Format("clients.client", Version) : Name;

    public bool ShowNamedVersion => !string.IsNullOrWhiteSpace(Name);

    public bool CanManage => _edit is not null && _remove is not null;

    public bool CanEdit => CanManage && !IsRunning && !IsStateUnknown;

    public bool CanRemove => CanManage && !IsRunning && !IsStateUnknown;

    public bool ShowOriginBadge => !IsRunning && !IsStateUnknown;

    public string StateText => IsRunning ? Loc.Get("clients.state.running")
        : IsStateUnknown ? Loc.Get("clients.state.unknown") : OriginText;

    [ObservableProperty]
    private bool _isInUse;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanRemove), nameof(ShowOriginBadge), nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(RemoveCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanRemove), nameof(ShowOriginBadge), nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(RemoveCommand))]
    private bool _isStateUnknown;

    public string Label => ShowNamedVersion
        ? $"{Title} · {Version} · {OriginText}"
        : $"{Title} · {OriginText}";

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit() => _edit?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => _remove?.Invoke(this);

    public void ApplyChanges(string? name, string path)
    {
        Name = name ?? string.Empty;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ShowNamedVersion));
        Path = path;
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Label));
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ShowNamedVersion));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(StateText));
    }
}
