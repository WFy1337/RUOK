using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using RUOK_App.ViewModels;

namespace RUOK_App.Views;

public sealed partial class BreathingView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _isLoaded;
    private bool _isShuttingDown;
    private bool _announcementQueued;
    private int _lifetimeVersion;
    public BreathingViewModel ViewModel { get; }

    public BreathingView(BreathingViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += ViewLoaded;
        Unloaded += ViewUnloaded;
    }

    private void ViewLoaded(object sender, RoutedEventArgs args)
    {
        if (_isLoaded || _isShuttingDown)
            return;

        _isLoaded = true;
        _timer.Tick += TimerTick;
        ViewModel.PropertyChanged += StateChanged;
        ViewModel.Tick();
        UpdateTimer();
    }

    private void ViewUnloaded(object sender, RoutedEventArgs args)
    {
        Detach();
        ViewModel.PauseWhenHidden();
    }

    public void Shutdown()
    {
        _isShuttingDown = true;
        BreathingOrb.Shutdown();
        Detach();
        ViewModel.PauseWhenHidden();
    }

    private void Detach()
    {
        _isLoaded = false;
        _lifetimeVersion++;
        _announcementQueued = false;
        _timer.Stop();
        _timer.Tick -= TimerTick;
        ViewModel.PropertyChanged -= StateChanged;
    }

    private void TimerTick(object? sender, object args)
    {
        if (_isLoaded && !_isShuttingDown && ViewModel.IsRunning)
            ViewModel.Tick();
        else
            _timer.Stop();
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BreathingViewModel.IsRunning))
            UpdateTimer();
        if (args.PropertyName == nameof(BreathingViewModel.Phase))
            QueuePhaseAnnouncement();
    }

    private void UpdateTimer()
    {
        if (_isLoaded && !_isShuttingDown && ViewModel.IsRunning && !_timer.IsEnabled)
            _timer.Start();
        else if (!_isLoaded || _isShuttingDown || !ViewModel.IsRunning)
            _timer.Stop();
    }

    private void QueuePhaseAnnouncement()
    {
        if (!_isLoaded || _isShuttingDown || _announcementQueued)
            return;

        _announcementQueued = true;
        var lifetimeVersion = _lifetimeVersion;
        // Defer native automation until binding callbacks finish, and discard work from an old view lifetime.
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (lifetimeVersion != _lifetimeVersion)
                return;

            _announcementQueued = false;
            if (!_isLoaded || _isShuttingDown || PhaseText.XamlRoot is null
                || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                return;

            var peer = FrameworkElementAutomationPeer.FromElement(PhaseText)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(PhaseText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }))
        {
            _announcementQueued = false;
        }
    }
}
