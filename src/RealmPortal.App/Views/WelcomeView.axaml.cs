using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Views;

public partial class WelcomeView : UserControl
{
    /// <summary>Side margins of the welcome layout: 48 px from 960 px wide, 24 px below (#107 layout).</summary>
    public const double WideMargin = 48;

    public const double CompactMargin = 24;

    private WelcomeViewModel? _viewModel;

    public WelcomeView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        // Tunnel: decide about Enter before a focused checkbox or language card handles it.
        AddHandler(KeyDownEvent, OnPreviewEnter, RoutingStrategies.Tunnel);
        SizeChanged += (_, e) => ApplyMargins(e.NewSize.Width);
        Loaded += (_, _) => FocusStep();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.StepFocusRequested -= OnStepFocusRequested;
        _viewModel = DataContext as WelcomeViewModel;
        if (_viewModel is not null)
            _viewModel.StepFocusRequested += OnStepFocusRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
            _viewModel.StepFocusRequested -= OnStepFocusRequested;
        _viewModel = null;
    }

    private void ApplyMargins(double width)
    {
        var wide = width >= MainWindow.WideFrom;
        var side = wide ? WideMargin : CompactMargin;
        // Compact: the steps start at the left margin so they never run into Skip on the right.
        ProgressStrip.HorizontalAlignment = wide ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        ProgressStrip.Margin = new Thickness(side, 10, side, 0);
        Body.Margin = new Thickness(side, 8, side, 0);
        Footer.Margin = new Thickness(side, 8, side, 8);
        SkipButton.Margin = new Thickness(0, 8, side, 0);
    }

    private void OnStepFocusRequested() =>
        Dispatcher.UIThread.Post(FocusStep, DispatcherPriority.Loaded);

    /// <summary>
    /// Focus the main element of the current step: the selected language card (step 1), the first client checkbox
    /// (step 2), otherwise the primary button. The last focused element is not remembered.
    /// </summary>
    private void FocusStep()
    {
        if (_viewModel is not { } vm)
            return;

        if (vm.IsServerFormOpen)
        {
            // The add-server form opened inside the welcome: start in its first field.
            ServerFormHost.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "NameBox")?.Focus(NavigationMethod.Tab);
            return;
        }

        Control? target = vm.Step switch
        {
            WelcomeViewModel.WelcomeStep.Language => vm.IsEnglish ? EnglishCard : SpanishCard,
            WelcomeViewModel.WelcomeStep.Clients => ClientList.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.IsEffectivelyVisible)
                                                    ?? (Control)ContinueClientsButton,
            _ => FinishButton,
        };
        target?.Focus(NavigationMethod.Tab);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _viewModel is not { } vm)
            return;

        if (vm.ServerForm is { } form)
        {
            // Esc closes an untouched form only; typed input is never discarded silently (Cancel stays explicit).
            if (e.Key == Key.Escape && form.IsPristine && form.CancelCommand.CanExecute(null))
            {
                form.CancelCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        var back = e.Key == Key.Escape
                   || (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt);
        if (back)
        {
            // Step 1: Esc does nothing (Skip needs an explicit click or Enter on the button).
            if (vm.BackCommand.CanExecute(null))
                vm.BackCommand.Execute(null);
            e.Handled = true;
            return;
        }
    }

    /// <summary>
    /// Enter runs the step's primary action, also from a client checkbox (it does not toggle it; Space does) and from a
    /// language card (it picks that card, then continues). Text fields and other focused buttons keep their own Enter.
    /// </summary>
    private void OnPreviewEnter(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || e.Handled
            || _viewModel is not { IsServerFormOpen: false } vm)
            return;

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox)
            return;
        if (focused == SpanishCard)
            vm.SelectSpanishCommand.Execute(null);
        else if (focused == EnglishCard)
            vm.SelectEnglishCommand.Execute(null);
        else if (focused is Button and not ToggleButton)
            return;

        var primary = vm.Step switch
        {
            WelcomeViewModel.WelcomeStep.Language => vm.ContinueLanguageCommand,
            WelcomeViewModel.WelcomeStep.Clients => vm.ContinueClientsCommand,
            _ => (System.Windows.Input.ICommand)vm.FinishCommand,
        };
        if (primary.CanExecute(null))
            primary.Execute(null);
        e.Handled = true;
    }
}
