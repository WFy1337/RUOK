using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RUOK.Application;
using RUOK_App.ViewModels;

namespace RUOK_App.Views;

public sealed partial class SettingsView : UserControl
{
    private bool _appearancePending;
    private bool _motionPending;
    public MainViewModel ViewModel { get; }
    public SettingsView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void AppearanceChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_appearancePending && sender is ComboBox { SelectedItem: Choice<AppearanceMode> selection }
            && ViewModel.ChangeAppearanceCommand.CanExecute(selection))
        {
            _appearancePending = true;
            try
            {
                // Let the native selection callback finish before changing its theme or enabled state.
                await Task.Yield();
                var current = ViewModel.SelectedAppearance;
                if (ViewModel.ChangeAppearanceCommand.CanExecute(current))
                    await ViewModel.ChangeAppearanceCommand.ExecuteAsync(current);
            }
            finally
            {
                _appearancePending = false;
            }
        }
    }

    private async void ReducedMotionChanged(object sender, RoutedEventArgs args)
    {
        if (!_motionPending && sender is ToggleSwitch toggle && ViewModel.ChangeReducedMotionCommand.CanExecute(toggle.IsOn))
        {
            _motionPending = true;
            try
            {
                await Task.Yield();
                if (ViewModel.ChangeReducedMotionCommand.CanExecute(ViewModel.ReducedMotion))
                    await ViewModel.ChangeReducedMotionCommand.ExecuteAsync(ViewModel.ReducedMotion);
            }
            finally
            {
                _motionPending = false;
            }
        }
    }
}
