using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RUOK_App.ViewModels;

namespace RUOK_App.Views;

public sealed partial class PulseCheckView : UserControl
{
    public MainViewModel ViewModel { get; }
    public PulseCheckView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void MoodChoicesSizeChanged(object sender, SizeChangedEventArgs args) => UpdateMoodLayout();

    private void MoodChoicesLoaded(object sender, RoutedEventArgs args) => UpdateMoodLayout();

    private void UpdateMoodLayout()
    {
        var width = MoodChoices.ActualWidth;
        if (width <= 0)
            return;
        var spacing = (double)MoodChoices.Resources["RadioButtonsColumnSpacing"];
        var minimum = (double)App.Current.Resources["MoodTileMinWidth"];
        var count = ViewModel.MoodOptions.Count;
        var capacity = Math.Clamp((int)((width + spacing) / (minimum + spacing)), 1, count);
        var rows = (count + capacity - 1) / capacity;
        var columns = (count + rows - 1) / rows;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        // The default radio layout sizes every column to its largest item, rather than stretching it.
        var tileWidth = Math.Floor((width - spacing * (columns - 1)) * scale / columns) / scale;
        MoodChoices.MaxColumns = columns;
        for (var index = 0; index < ViewModel.MoodOptions.Count; index++)
            if (MoodChoices.ContainerFromIndex(index) is RadioButton tile && tile.Width != tileWidth)
                tile.Width = tileWidth;
    }
}
