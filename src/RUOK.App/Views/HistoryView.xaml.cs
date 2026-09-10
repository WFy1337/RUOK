using Microsoft.UI.Xaml.Controls;
using RUOK_App.ViewModels;

namespace RUOK_App.Views;

public sealed partial class HistoryView : UserControl
{
    public MainViewModel ViewModel { get; }
    public HistoryView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
