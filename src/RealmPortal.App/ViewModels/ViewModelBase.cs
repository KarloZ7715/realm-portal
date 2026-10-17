using CommunityToolkit.Mvvm.ComponentModel;

namespace RealmPortal.App.ViewModels;

/// <summary>Base of the screens shown in the content area.</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>Text of the status bar for this screen.</summary>
    public abstract string StatusText { get; }

    /// <summary>Re-reads translated text after a language change.</summary>
    public virtual void RefreshText()
    {
    }
}
