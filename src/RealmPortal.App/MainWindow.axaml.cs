using Avalonia.LogicalTree;
using Avalonia.Controls;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App;

public partial class MainWindow : Window
{
    /// <summary>Width from which the sidebar shows labels and the non-affiliation note.</summary>
    public const double WideFrom = 960;

    public MainWindow() : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SizeChanged += (_, e) => UpdateSidebar(e.NewSize.Width);
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
