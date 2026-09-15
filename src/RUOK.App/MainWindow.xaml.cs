using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using RUOK.Application;
using RUOK.Infrastructure;
using RUOK_App.Services;
using RUOK_App.Resources;
using RUOK_App.ViewModels;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace RUOK_App;

public sealed partial class MainWindow : Window
{
    private readonly ServiceProvider _services;
    private readonly MainViewModel _viewModel;
    private readonly BreathingViewModel _breathing;
    private readonly MainPage _page;
    private readonly UISettings _uiSettings = new();
    private readonly ThemeSettings _themeSettings;
    private XamlRoot? _xamlRoot;
    private double _windowScale;
    private bool _closed;
    private bool _exiting;
    private TrayIcon? _tray;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _reminderTimer;

    public MainWindow(WindowsNotifications notifications)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(AppRuntime.AssetPath("AppIcon.ico"));
        AppWindow.Resize(new SizeInt32(1180, 860));
        _themeSettings = ThemeSettings.CreateForWindowId(AppWindow.Id);

        var dataPath = AppRuntime.DataPath;
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(TimeZoneInfo.Local);
        services.AddSingleton<IDataProtector, DpapiDataProtector>();
        services.AddSingleton(provider => new SqliteWellbeingStore(dataPath, provider.GetRequiredService<IDataProtector>()));
        services.AddSingleton<IWellbeingStore>(provider => provider.GetRequiredService<SqliteWellbeingStore>());
        services.AddSingleton<IEncouragementStore>(provider => provider.GetRequiredService<SqliteWellbeingStore>());
        services.AddSingleton<WellbeingService>();
        services.AddSingleton<EncouragementService>();
        services.AddSingleton<ICsvExporter, CsvExporter>();
        services.AddSingleton<IUserDialogs>(new UserDialogs(this));
        services.AddSingleton(provider => new MainViewModel(
            provider.GetRequiredService<WellbeingService>(), provider.GetRequiredService<ICsvExporter>(),
            provider.GetRequiredService<IUserDialogs>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<TimeZoneInfo>(), dataPath, notifications,
            provider.GetRequiredService<EncouragementService>()));
        services.AddSingleton<BreathingViewModel>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _viewModel = _services.GetRequiredService<MainViewModel>();
        _breathing = _services.GetRequiredService<BreathingViewModel>();
        _viewModel.SettingsChanged += ApplySettings;
        _page = new MainPage(_viewModel, _breathing);
        _reminderTimer = DispatcherQueue.CreateTimer();
        _reminderTimer.Interval = TimeSpan.FromSeconds(15);
        _reminderTimer.Tick += ReminderTick;
        RootFrame.Content = _page;
        WindowRoot.Loaded += ContentLoaded;
        WindowRoot.ActualThemeChanged += ContentThemeChanged;
        RefreshAccessibility();
        _themeSettings.Changed += ContrastChanged;
        _uiSettings.AnimationsEnabledChanged += AnimationsChanged;
        AppWindow.Closing += WindowClosing;
        Activated += (_, args) =>
        {
            if (_closed)
                return;
            RefreshAccessibility();
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                _breathing.PauseWhenHidden();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _reminderTimer.Stop();
            _reminderTimer.Tick -= ReminderTick;
            AppWindow.Closing -= WindowClosing;
            _tray?.Dispose();
            _tray = null;
            _page.Shutdown();
            WindowRoot.Loaded -= ContentLoaded;
            WindowRoot.ActualThemeChanged -= ContentThemeChanged;
            if (_xamlRoot is not null)
                _xamlRoot.Changed -= RootChanged;
            _themeSettings.Changed -= ContrastChanged;
            _uiSettings.AnimationsEnabledChanged -= AnimationsChanged;
            _viewModel.SettingsChanged -= ApplySettings;
            _services.Dispose();
        };
    }

    public async Task InitializeAsync()
    {
        await _page.InitializeAsync();
        if (!_closed)
            _reminderTimer.Start();
    }

    public void ShowWindow()
    {
        if (_closed)
            return;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();
        Activate();
        TrayIcon.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    public void RequestExit()
    {
        _exiting = true;
        Close();
    }

    public async Task HandleNotificationAsync(NotificationIntent? intent)
    {
        await InitializeAsync();
        if (_closed)
            return;
        if (intent is null)
        {
            _viewModel.ShowStatus(UiText.Get("NotificationInvalid"), error: true);
            ShowWindow();
            return;
        }
        var foreground = !_viewModel.IsReady || await _viewModel.HandleNotificationAsync(intent);
        if (foreground)
            ShowWindow();
        else if (!Visible && !_viewModel.ShouldStayInTray)
            RequestExit();
    }

    private async void ReminderTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_closed)
            await _viewModel.CheckReminderAsync();
    }

    private void WindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting || !_viewModel.ShouldStayInTray)
            return;
        EnsureTray();
        if (_tray is null)
        {
            // Never leave a hidden process without a usable way to reopen or quit it.
            args.Cancel = true;
            _viewModel.ShowStatus(UiText.Get("TrayUnavailable"), error: true);
            return;
        }
        args.Cancel = true;
        _breathing.PauseWhenHidden();
        AppWindow.Hide();
    }

    private void EnsureTray()
    {
        if (_tray is not null || _closed)
            return;
        try
        {
            _tray = new TrayIcon();
            _tray.OpenRequested += ShowWindow;
            _tray.QuitRequested += RequestExit;
            _tray.Error += message =>
            {
                _viewModel.ShowStatus(message, error: true);
                ShowWindow();
            };
        }
        catch (Win32Exception)
        {
            _viewModel.ShowStatus(UiText.Get("TrayUnavailable"), error: true);
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        if (_closed)
            return;
        WindowRoot.RequestedTheme = settings.Appearance switch
        {
            AppearanceMode.Light => ElementTheme.Light,
            AppearanceMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateTitleBarTheme();
        _breathing.ReducedMotion = settings.ReducedMotion;
        if (settings.Notifications.KeepInTray && _viewModel.HasScheduledNotifications)
            EnsureTray();
        else if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    private void ContentLoaded(object sender, RoutedEventArgs args)
    {
        _xamlRoot = WindowRoot.XamlRoot;
        _xamlRoot.Changed += RootChanged;
        UpdateWindowBounds();
        UpdateTitleBarTheme();
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateWindowBounds();

    private void UpdateWindowBounds()
    {
        if (_closed || _xamlRoot is null || _windowScale == _xamlRoot.RasterizationScale)
            return;
        _windowScale = _xamlRoot.RasterizationScale;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(600 * _windowScale);
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(440 * _windowScale);
        }
    }

    private void ContentThemeChanged(FrameworkElement sender, object args) => UpdateTitleBarTheme();

    private void UpdateTitleBarTheme()
    {
        if (!_closed)
            AppWindow.TitleBar.PreferredTheme = WindowRoot.ActualTheme == ElementTheme.Dark
                ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }

    private void ContrastChanged(ThemeSettings sender, object args) => QueueAccessibilityRefresh();

    private void AnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) => QueueAccessibilityRefresh();

    private void QueueAccessibilityRefresh() =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed)
                RefreshAccessibility();
        });

    private void RefreshAccessibility()
    {
        _breathing.HighContrast = _themeSettings.HighContrast;
        _breathing.SystemReducedMotion = !_uiSettings.AnimationsEnabled || _themeSettings.HighContrast;
    }
}
