using Microsoft.UI.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RUOK_App.Controls;

public sealed partial class MoodFace : UserControl
{
    private readonly UISettings _uiSettings = new();
    private ThemeSettings? _themeSettings;
    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score), typeof(int), typeof(MoodFace), new PropertyMetadata(3, ScoreChanged));

    public int Score { get => (int)GetValue(ScoreProperty); set => SetValue(ScoreProperty, value); }

    public MoodFace()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => UpdateFace();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _themeSettings = ThemeSettings.CreateForWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        _themeSettings.Changed += ContrastChanged;
        UpdateFace();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_themeSettings is not null)
            _themeSettings.Changed -= ContrastChanged;
        _themeSettings = null;
    }

    private static void ScoreChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((MoodFace)sender).UpdateFace();

    private void ContrastChanged(ThemeSettings sender, object args) =>
        DispatcherQueue.TryEnqueue(UpdateFace);

    private void UpdateFace()
    {
        if (MouthCurve is null)
            return;
        MouthCurve.Point1 = new Point(26, Score switch { 1 => 15, 2 => 24, 4 => 42, 5 => 50, _ => 33 });
        if (_themeSettings?.HighContrast == true)
        {
            FaceRing.Stroke = new SolidColorBrush(_uiSettings.GetColorValue(UIColorType.Foreground));
            return;
        }
        uint[] colors = ActualTheme == ElementTheme.Dark
            ? [0xEEA0AA, 0xEEC18D, 0xE8D389, 0x8ED5BB, 0x79DDBE]
            : [0x91343E, 0x895309, 0x786500, 0x24765B, 0x126350];
        var color = colors[Math.Clamp(Score, 1, 5) - 1];
        FaceRing.Stroke = new SolidColorBrush(new Color
        {
            A = 255, R = (byte)(color >> 16), G = (byte)(color >> 8), B = (byte)color
        });
    }
}
