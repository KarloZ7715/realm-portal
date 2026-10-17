using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RealmPortal.App.Controls;

public enum BannerSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A dashed-outline notice with an icon, a bold title (<see cref="HeaderedContentControl.Header"/>) and a body
/// (<see cref="ContentControl.Content"/>). The look follows <see cref="Severity"/> through the pseudo-classes :info, :warning and :error.
/// </summary>
public class Banner : HeaderedContentControl
{
    public static readonly StyledProperty<BannerSeverity> SeverityProperty =
        AvaloniaProperty.Register<Banner, BannerSeverity>(nameof(Severity));

    public Banner() => UpdateClasses();

    public BannerSeverity Severity { get => GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeverityProperty)
        {
            UpdateClasses();
        }
    }

    private void UpdateClasses()
    {
        PseudoClasses.Set(":info", Severity == BannerSeverity.Info);
        PseudoClasses.Set(":warning", Severity == BannerSeverity.Warning);
        PseudoClasses.Set(":error", Severity == BannerSeverity.Error);
    }
}
