using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RUOK_App.ViewModels;

namespace RUOK_App.Views;

public sealed partial class DashboardView : UserControl
{
    private bool? _wideMetrics;
    public MainViewModel ViewModel { get; }
    public DashboardView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void HeroSizeChanged(object sender, SizeChangedEventArgs args) =>
        HeroFace.Visibility = args.NewSize.Width >= 500 ? Visibility.Visible : Visibility.Collapsed;

    private void MetricsSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var wide = args.NewSize.Width >= 804;
        if (_wideMetrics == wide)
            return;
        _wideMetrics = wide;
        Grid.SetColumnSpan(AverageCard, wide ? 1 : 3);
        Grid.SetRow(CountCard, wide ? 0 : 1);
        Grid.SetColumn(CountCard, wide ? 1 : 0);
        Grid.SetColumnSpan(CountCard, wide ? 1 : 3);
        Grid.SetRow(LatestCard, wide ? 0 : 2);
        Grid.SetColumn(LatestCard, wide ? 2 : 0);
        Grid.SetColumnSpan(LatestCard, wide ? 1 : 3);
    }
}
