using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

/// <summary>A screen that is not built yet (Settings in this phase).</summary>
public sealed class PlaceholderViewModel(string titleKey) : ViewModelBase
{
    public string Title => Loc.Get(titleKey);

    public string Body => Loc.Get("placeholder.body");

    public override void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
    }

    public override string StatusText => Loc.Get(titleKey);
}
