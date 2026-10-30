using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        if (_viewModel is not { IsStepVisible: true } vm)
            return;

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
            if (e.Key == Key.Escape && form.CancelCommand.CanExecute(null))
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

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox { AcceptsReturn: true })
        {
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
}
