using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace RealmPortal.App.Controls;

/// <summary>
/// Small status tag ("ACTIVE", "READY", ...) with a glyph and an upper-cased label.
/// The look is selected through pseudo-classes (:active, :ready, :incompatible, :no-response, :inactive) set from <see cref="Status"/>.
/// </summary>
public class Badge : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<Badge, string?>(nameof(Text));

    public static readonly StyledProperty<ServerStatus> StatusProperty =
        AvaloniaProperty.Register<Badge, ServerStatus>(nameof(Status));

    public static readonly DirectProperty<Badge, string?> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<Badge, string?>(nameof(DisplayText), badge => badge.DisplayText);

    private string? _displayText;

    public Badge() => UpdateStatusClasses();

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public ServerStatus Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// <summary>The label as shown: upper case, so translators supply normal text.</summary>
    public string? DisplayText
    {
        get => _displayText;
        private set => SetAndRaise(DisplayTextProperty, ref _displayText, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            DisplayText = Text?.ToUpper(CultureInfo.CurrentUICulture);
        }
        else if (change.Property == StatusProperty)
        {
            UpdateStatusClasses();
        }
    }

    private void UpdateStatusClasses()
    {
        PseudoClasses.Set(":active", Status == ServerStatus.Active);
        PseudoClasses.Set(":ready", Status == ServerStatus.Ready);
        PseudoClasses.Set(":incompatible", Status == ServerStatus.Incompatible);
        PseudoClasses.Set(":no-response", Status == ServerStatus.NoResponse);
        PseudoClasses.Set(":inactive", Status == ServerStatus.Inactive);
    }
}
