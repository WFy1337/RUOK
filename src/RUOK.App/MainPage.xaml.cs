using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RUOK_App.ViewModels;
using RUOK_App.Views;

namespace RUOK_App;

public sealed partial class MainPage : Page
{
    private readonly BreathingViewModel _breathing;
    private readonly Dictionary<Screen, UserControl> _pages = [];
    private Task? _initialization;
    public MainViewModel ViewModel { get; }

    public MainPage(MainViewModel viewModel, BreathingViewModel breathing)
    {
        ViewModel = viewModel;
        _breathing = breathing;
        InitializeComponent();
        ViewModel.NavigationRequested += NavigateTo;
        Navigation.SelectedItem = DashboardItem;
        Loaded += async (_, _) => await InitializeAsync();
    }

    public Task InitializeAsync() => _initialization ??= ViewModel.InitializeAsync();

    private double RefreshButtonWidth(bool isPaneOpen, double openLength, double compactLength) =>
        (isPaneOpen ? openLength : compactLength) - 8;

    private GridLength RefreshIconColumn(double compactLength) => new(compactLength - 8);

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Enum.TryParse<Screen>(tag, out var screen))
            ShowPage(screen);
    }

    private void NavigateTo(Screen screen)
    {
        Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>().Single(item => Equals(item.Tag, screen.ToString()));
    }

    private void ShowPage(Screen screen)
    {
        if (!_pages.TryGetValue(screen, out var page))
        {
            page = screen switch
            {
                Screen.Dashboard => new DashboardView(ViewModel),
                Screen.PulseCheck => new PulseCheckView(ViewModel),
                Screen.History => new HistoryView(ViewModel),
                Screen.Breathing => new BreathingView(_breathing),
                Screen.Settings => new SettingsView(ViewModel),
                Screen.About => new AboutView(),
                _ => throw new ArgumentOutOfRangeException(nameof(screen))
            };
            _pages.Add(screen, page);
        }
        PageHost.Content = page;
    }

    public void Shutdown()
    {
        if (_pages.TryGetValue(Screen.Breathing, out var page) && page is BreathingView breathingView)
            breathingView.Shutdown();
        else
            _breathing.PauseWhenHidden();
        ViewModel.NavigationRequested -= NavigateTo;
    }
}
