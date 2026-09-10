using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using RUOK.Application;
using RUOK_App.Services;

namespace RUOK_App;

public partial class App : Application
{
    private MainWindow? _window;
    private readonly AppInstance _instance;
    private readonly WindowsNotifications _notifications = new();
    
    public App(AppInstance instance)
    {
        _instance = instance;
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_notifications);
        _instance.Activated += InstanceActivated;
        AppNotificationManager.Default.NotificationInvoked += NotificationInvoked;
        _notifications.Register();
        var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
        _window.Closed += (_, _) =>
        {
            _instance.Activated -= InstanceActivated;
            AppNotificationManager.Default.NotificationInvoked -= NotificationInvoked;
            _notifications.Unregister();
        };
        var comLaunch = Environment.GetCommandLineArgs().Any(value =>
            value.Contains("----AppNotificationActivated:", StringComparison.Ordinal));
        if (activated.Data is AppNotificationActivatedEventArgs notification)
            NotificationInvoked(AppNotificationManager.Default, notification);
        else if (!comLaunch || !_notifications.IsRegistered)
            _window.ShowWindow();
        await _window.InitializeAsync();
    }

    private void InstanceActivated(object? sender, AppActivationArguments args)
    {
        if (args.Data is AppNotificationActivatedEventArgs notification)
            NotificationInvoked(AppNotificationManager.Default, notification);
        else
            _window?.DispatcherQueue.TryEnqueue(() => _window.ShowWindow());
    }

    private void NotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        NotificationIntent.TryParse(args.Argument, out var intent);
        _window?.DispatcherQueue.TryEnqueue(async () => await _window.HandleNotificationAsync(intent));
    }
}
