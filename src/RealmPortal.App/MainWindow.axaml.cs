using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App;

public partial class MainWindow : Window
{
    /// <summary>Width from which the sidebar shows labels and the non-affiliation note.</summary>
    public const double WideFrom = 960;

    private IInputElement? _focusBeforeDialog;
    private bool _closeConfirmed;

    /// <summary>
    /// The previewer calls this constructor. Design mode uses the demo backend so it does not open the developer's portal config.
    /// </summary>
    public MainWindow() : this(Design.IsDesignMode ? new MainViewModel() : new MainViewModel(PortalSession.Create()))
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.DialogOpening += CaptureFocusBeforeDialog;
        viewModel.DialogClosed += RestoreFocusAfterDialog;
        // A binding does not push an unchanged source value back, so re-select the held section by hand
        // after the ListBox finishes its own selection change.
        viewModel.SectionChangeHeld += () => Dispatcher.UIThread.Post(() => Nav.SelectedIndex = viewModel.SectionIndex);
        Opened += (_, _) => Platform.X11WindowIcon.TrySet(this);
        SizeChanged += (_, e) => UpdateSidebar(e.NewSize.Width);
        Closing += (_, e) => OnClosingWithUnsavedSettings(viewModel, e);
    }

    /// <summary>
    /// Closing with unsaved Settings asks the same "Discard unsaved changes?" question as leaving the screen.
    /// The close is cancelled while the dialog is open and repeated only after "Discard changes". A system shutdown is never held.
    /// </summary>
    private void OnClosingWithUnsavedSettings(MainViewModel viewModel, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !viewModel.Settings.IsDirty || e.CloseReason == WindowCloseReason.OSShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (viewModel.Dialog is not null)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            if (await viewModel.ConfirmDiscardSettingsAsync())
            {
                _closeConfirmed = true;
                Close();
            }
        });
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
