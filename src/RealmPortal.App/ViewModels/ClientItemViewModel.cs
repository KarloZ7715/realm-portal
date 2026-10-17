using CommunityToolkit.Mvvm.ComponentModel;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

/// <summary>One detected (or hand-picked) game client row.</summary>
public sealed partial class ClientItemViewModel(ClientSeed seed) : ObservableObject
{
    public string Version { get; } = seed.Version;

    public string Path { get; } = seed.Path;

    public ClientOrigin Origin { get; } = seed.Origin;

    [ObservableProperty]
    private bool _isInUse = seed.InUse;

    /// <summary>"Cliente 3.3.5a (12340)".</summary>
    public string Title => Loc.Format("clients.client", Version);

    public string OriginText => Loc.Get($"clients.origin.{Origin.ToString().ToLowerInvariant()}");

    /// <summary>"Cliente 3.3.5a (12340) · Lutris": how the add-server form lists it.</summary>
    public string Label => $"{Title} · {OriginText}";

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(Label));
    }
}
