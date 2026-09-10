using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace RUOK_App.Controls;

public sealed partial class NebulaOrb : UserControl
{
    private NebulaScene? _scene;
    private XamlRoot? _root;
    private ScrollViewer? _scrollViewport;
    private bool _inViewport = true;
    private bool _shuttingDown;
    private bool _imageFailed;
    private int _lifetimeVersion;

    public static readonly DependencyProperty BreathingScaleProperty = DependencyProperty.Register(
        nameof(BreathingScale), typeof(double), typeof(NebulaOrb), new PropertyMetadata(.76, VisualPropertyChanged));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(
        nameof(MotionEnabled), typeof(bool), typeof(NebulaOrb), new PropertyMetadata(true, VisualPropertyChanged));
    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.Register(
        nameof(HighContrast), typeof(bool), typeof(NebulaOrb), new PropertyMetadata(false, VisualPropertyChanged));

    public double BreathingScale { get => (double)GetValue(BreathingScaleProperty); set => SetValue(BreathingScaleProperty, value); }
    public bool MotionEnabled { get => (bool)GetValue(MotionEnabledProperty); set => SetValue(MotionEnabledProperty, value); }
    public bool HighContrast { get => (bool)GetValue(HighContrastProperty); set => SetValue(HighContrastProperty, value); }

    public NebulaOrb()
    {
        InitializeComponent();
        Loaded += OrbLoaded;
        Unloaded += OrbUnloaded;
        EffectiveViewportChanged += ViewportChanged;
        SizeChanged += ViewportSizeChanged;
    }

    private void OrbLoaded(object sender, RoutedEventArgs args)
    {
        if (_shuttingDown || _scene is not null)
            return;
        _root = XamlRoot;
        _root.Changed += HostChanged;
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer viewport)
                continue;
            _scrollViewport = viewport;
            viewport.ViewChanged += ScrollChanged;
            viewport.SizeChanged += ViewportSizeChanged;
            break;
        }
        _imageFailed = false;
        VisualError.Visibility = Visibility.Collapsed;
        var lifetime = ++_lifetimeVersion;
        try
        {
            _scene = NebulaScene.Create(CompositionHost, () => DispatcherQueue.TryEnqueue(() =>
            {
                if (_lifetimeVersion == lifetime && !_shuttingDown && IsLoaded)
                    ShowVisualError();
            }));
        }
        catch (COMException)
        {
            ShowVisualError();
        }
        UpdateVisual();
    }

    private void OrbUnloaded(object sender, RoutedEventArgs args) => ReleaseScene();
    private void HostChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateVisual();
    private void ScrollChanged(object? sender, ScrollViewerViewChangedEventArgs args) => UpdateVisual();
    private void ViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdateVisual();

    private void ViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var viewport = args.EffectiveViewport;
        _inViewport = viewport.Width > 0 && viewport.Height > 0
            && viewport.Right > 0 && viewport.Bottom > 0
            && viewport.Left < ActualWidth && viewport.Top < ActualHeight;
        UpdateVisual();
    }

    private static void VisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((NebulaOrb)sender).UpdateVisual();

    private void UpdateVisual()
    {
        if (_shuttingDown || CompositionHost is null)
            return;
        CompositionHost.Visibility = HighContrast ? Visibility.Collapsed : Visibility.Visible;
        ContrastVisual.Visibility = HighContrast ? Visibility.Visible : Visibility.Collapsed;
        _scene?.SetMotion(MotionEnabled && !HighContrast && !_imageFailed && IsLoaded
            && _inViewport && _root?.IsHostVisible == true && IsInsideScrollViewport());
        _scene?.SetScale(BreathingScale);
    }

    private bool IsInsideScrollViewport()
    {
        if (_scrollViewport is null)
            return true;
        // Check the scroll clip as well as effective-viewport notifications.
        var bounds = TransformToVisual(_scrollViewport).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
        return bounds.Right > 0 && bounds.Bottom > 0
            && bounds.Left < _scrollViewport.ViewportWidth && bounds.Top < _scrollViewport.ViewportHeight;
    }

    private void ShowVisualError()
    {
        _imageFailed = true;
        VisualError.Visibility = Visibility.Visible;
        _scene?.SetMotion(false);
    }

    public void Shutdown()
    {
        _shuttingDown = true;
        EffectiveViewportChanged -= ViewportChanged;
        SizeChanged -= ViewportSizeChanged;
        ReleaseScene();
    }

    private void ReleaseScene()
    {
        _lifetimeVersion++;
        if (_scrollViewport is not null)
        {
            _scrollViewport.ViewChanged -= ScrollChanged;
            _scrollViewport.SizeChanged -= ViewportSizeChanged;
        }
        _scrollViewport = null;
        if (_root is not null)
            _root.Changed -= HostChanged;
        _root = null;
        _scene?.Dispose();
        _scene = null;
    }
}
