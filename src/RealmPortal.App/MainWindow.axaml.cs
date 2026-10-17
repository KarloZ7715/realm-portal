using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App;

public partial class MainWindow : Window
{
    /// <summary>Width from which the sidebar shows labels and the non-affiliation note.</summary>
    public const double WideFrom = 960;

    private IInputElement? _focusBeforeDialog;

    public MainWindow() : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.DialogOpening += CaptureFocusBeforeDialog;
        viewModel.DialogClosed += RestoreFocusAfterDialog;
        Opened += (_, _) => Platform.X11WindowIcon.TrySet(this);
        SizeChanged += (_, e) => UpdateSidebar(e.NewSize.Width);
    }

    private void CaptureFocusBeforeDialog() =>
        _focusBeforeDialog = FocusManager?.GetFocusedElement();

    private void RestoreFocusAfterDialog()
    {
        if (_focusBeforeDialog is not { Focusable: true } restore)
        {
            _focusBeforeDialog = null;
            return;
        }

        _focusBeforeDialog = null;
        Dispatcher.UIThread.Post(() => restore.Focus(), DispatcherPriority.Loaded);
    }

    private void UpdateSidebar(double width)
    {
        var wide = width >= WideFrom;
        Sidebar.Width = wide ? 224 : 56;
        Nav.Classes.Set("compact", !wide);
        DisclaimerText.IsVisible = wide;
        foreach (var label in Nav.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("navlabel")))
        {
            label.IsVisible = wide;
        }
    }
}
