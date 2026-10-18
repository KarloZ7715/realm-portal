using CommunityToolkit.Mvvm.ComponentModel;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.Core.Service;

namespace RealmPortal.App.ViewModels;

/// <summary>One detected (or hand-picked) game client row.</summary>
public sealed partial class ClientItemViewModel : ObservableObject
{
    private ClientItemViewModel(string? id, string version, string path, ClientOrigin origin, bool inUse)
    {
        Id = id;
        Version = version;
        Path = path;
        Origin = origin;
        _isInUse = inUse;
    }

    public ClientItemViewModel(ClientSeed seed)
        : this(null, seed.Version, seed.Path, seed.Origin, seed.InUse)
    {
    }

    public static ClientItemViewModel FromEntry(ClientEntry entry, bool inUse)
    {
        var version = entry.Version?.ToString() ?? entry.Error?.Code.ToString() ?? "?";
        return new ClientItemViewModel(entry.Id, version, entry.Path, ClientOrigin.Manual, inUse);
    }

    public string? Id { get; }

    public string Version { get; }

    public string Path { get; }

    public ClientOrigin Origin { get; }

    [ObservableProperty]
    private bool _isInUse;

    public string Title => Loc.Format("clients.client", Version);

    public string OriginText => Loc.Get($"clients.origin.{Origin.ToString().ToLowerInvariant()}");

    public string Label => $"{Title} · {OriginText}";

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(Label));
    }
}
