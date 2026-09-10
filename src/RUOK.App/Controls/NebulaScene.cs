using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace RUOK_App.Controls;

internal sealed class NebulaScene : IDisposable
{
    private readonly FrameworkElement _host;
    private readonly Compositor _compositor;
    private readonly List<IDisposable> _resources = [];
    private readonly List<(CompositionObject Target, string Property, AnimationController Controller)> _animations = [];
    private readonly List<LoadedImageSurface> _surfaces = [];
    private readonly ContainerVisual _root;
    private readonly ImplicitAnimationCollection _scaleTransitions;
    private readonly Action _imageFailed;
    private bool _moving;
    private bool _disposed;
    private float _scale = .76f;

    private NebulaScene(FrameworkElement host, Action imageFailed)
    {
        _host = host;
        _imageFailed = imageFailed;
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _root = Own(_compositor.CreateContainerVisual());
        _scaleTransitions = Own(_compositor.CreateImplicitAnimationCollection());
    }

    public static NebulaScene Create(FrameworkElement host, Action imageFailed)
    {
        var scene = new NebulaScene(host, imageFailed);
        var initialized = false;
        try
        {
            scene.Build();
            initialized = true;
            return scene;
        }
        finally
        {
            if (!initialized)
                scene.Dispose();
        }
    }

    private void Build()
    {
        _root.Size = new Vector2(320);
        _root.CenterPoint = new Vector3(160, 160, 0);
        _root.Scale = new Vector3(_scale, _scale, 1);

        var transition = Own(_compositor.CreateVector3KeyFrameAnimation());
        transition.Target = nameof(Visual.Scale);
        transition.Duration = TimeSpan.FromMilliseconds(50);
        transition.InsertExpressionKeyFrame(1, "this.FinalValue", Own(_compositor.CreateLinearEasingFunction()));
        _scaleTransitions[nameof(Visual.Scale)] = transition;

        AddGlow(_root, new Vector2(320), Vector2.Zero, Radial(
            (0, Ink(75, 55, 220, 0)), (.70f, Ink(86, 94, 247, 8)),
            (.79f, Ink(86, 126, 255, 55)), (.88f, Ink(91, 79, 215, 18)), (1, Ink(70, 85, 190, 0))));

        var body = Own(_compositor.CreateContainerVisual());
        body.Size = new Vector2(256);
        body.Offset = new Vector3(32, 32, 0);
        var circle = Own(_compositor.CreateEllipseGeometry());
        circle.Center = new Vector2(128);
        circle.Radius = new Vector2(128);
        body.Clip = Own(_compositor.CreateGeometricClip(circle));
        _root.Children.InsertAtTop(body);
        AddGlow(body, new Vector2(256), Vector2.Zero, Radial(
            (0, Ink(51, 31, 91)), (.60f, Ink(22, 22, 62)), (1, Ink(6, 10, 29))));

        var dustA = LoadMask("DustA.png");
        var dustB = LoadMask("DustB.png");
        AddCloud(body, dustA, new Vector2(352, 318), new Vector2(-48, -43), 18, 43,
            Ink(87, 232, 255), Ink(23, 90, 214), .90f);
        AddCloud(body, dustB, new Vector2(325, 350), new Vector2(-38, -55), 147, -57,
            Ink(217, 119, 252), Ink(104, 42, 200), .82f);
        AddCloud(body, dustA, new Vector2(306, 342), new Vector2(-28, -29), 253, 71,
            Ink(255, 113, 193), Ink(106, 46, 181), .63f);
        AddCloud(body, dustB, new Vector2(310, 294), new Vector2(-8, -4), 310, -89,
            Ink(128, 255, 216), Ink(15, 147, 184), .68f);

        var core = AddGlow(body, new Vector2(156, 112), new Vector2(50, 70), Radial(
            (0, Ink(225, 243, 255, 145)), (.25f, Ink(113, 218, 255, 85)),
            (.62f, Ink(139, 143, 255, 35)), (1, Ink(112, 145, 255, 0))));
        AnimateFloat(core, nameof(Visual.Opacity), .58f, .90f, .58f, 11);
        AnimateDrift(core, new Vector3(50, 70, 0), new Vector3(58, 87, 0), 19);
        AddStars(body);

        var shading = Radial((0, Ink(255, 255, 255, 22)), (.42f, Ink(207, 238, 255, 0)),
            (.79f, Ink(7, 11, 28, 2)), (1, Ink(4, 6, 24, 160)));
        AddGlow(body, new Vector2(256), Vector2.Zero, shading);
        AddGlow(body, new Vector2(154, 75), new Vector2(32, 1), Radial(
            (0, Ink(228, 231, 255, 44)), (.50f, Ink(151, 183, 255, 16)), (1, Ink(149, 188, 255, 0))));
        AddRim();

        foreach (var animation in _animations)
            animation.Controller.Pause();
        ElementCompositionPreview.SetElementChildVisual(_host, _root);
    }

    public void SetMotion(bool enabled)
    {
        if (_disposed || _moving == enabled)
            return;
        _moving = enabled;
        foreach (var animation in _animations)
            if (enabled)
                animation.Controller.Resume();
            else
                animation.Controller.Pause();
        _root.ImplicitAnimations = enabled ? _scaleTransitions : null;
        if (!enabled)
        {
            _root.StopAnimation(nameof(Visual.Scale));
            _root.Scale = new Vector3(_scale, _scale, 1);
        }
    }

    public void SetScale(double scale)
    {
        if (_disposed)
            return;
        _scale = (float)scale;
        _root.Scale = new Vector3(_scale, _scale, 1);
    }

    private LoadedImageSurface LoadMask(string name)
    {
        var surface = Own(LoadedImageSurface.StartLoadFromUri(new Uri($"ms-appx:///Assets/Nebula/{name}")));
        _surfaces.Add(surface);
        surface.LoadCompleted += ImageLoaded;
        return surface;
    }

    private void ImageLoaded(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args)
    {
        if (!_disposed && args.Status != LoadedImageSourceLoadStatus.Success)
            _imageFailed();
    }

    private void AddCloud(ContainerVisual parent, LoadedImageSurface mask, Vector2 size, Vector2 offset,
        float angle, double seconds, Color bright, Color deep, float opacity)
    {
        var surface = Own(_compositor.CreateSurfaceBrush(mask));
        surface.Stretch = CompositionStretch.Fill;
        var brush = Own(_compositor.CreateMaskBrush());
        brush.Mask = surface;
        brush.Source = Radial((0, bright), (.48f, bright), (1, deep));
        var cloud = AddGlow(parent, size, offset, brush);
        cloud.CenterPoint = new Vector3(size / 2, 0);
        cloud.Opacity = opacity;
        AnimateFloat(cloud, nameof(Visual.RotationAngleInDegrees), angle, angle + 180 * Math.Sign(seconds),
            angle + 360 * Math.Sign(seconds), Math.Abs(seconds), linear: true);
        AnimateDrift(cloud, new Vector3(offset, 0), new Vector3(offset + new Vector2(11, -8), 0),
            Math.Abs(seconds) * .61);
    }

    private void AddStars(ContainerVisual parent)
    {
        var stars = Own(_compositor.CreateContainerVisual());
        stars.Size = new Vector2(256);
        stars.CenterPoint = new Vector3(128, 128, 0);
        parent.Children.InsertAtTop(stars);
        var random = new Random(617);
        var brush = Radial((0, Ink(238, 247, 255, 190)), (.35f, Ink(163, 209, 255, 105)), (1, Ink(150, 208, 255, 0)));
        for (var index = 0; index < 34; index++)
        {
            var angle = random.NextDouble() * Math.Tau;
            var radius = Math.Sqrt(random.NextDouble()) * 113;
            var size = (float)(1.3 + random.NextDouble() * 2.5);
            AddGlow(stars, new Vector2(size),
                new Vector2(128 + (float)Math.Cos(angle) * (float)radius, 128 + (float)Math.Sin(angle) * (float)radius), brush);
        }
        AnimateFloat(stars, nameof(Visual.RotationAngleInDegrees), 0, 180, 360, 180, linear: true);
        AnimateFloat(stars, nameof(Visual.Opacity), .55f, .88f, .55f, 13);
    }

    private void AddRim()
    {
        var brush = Own(_compositor.CreateLinearGradientBrush());
        brush.StartPoint = Vector2.Zero;
        brush.EndPoint = Vector2.One;
        foreach (var (position, color) in new[]
        {
            (0f, Ink(244, 205, 255, 190)), (.28f, Ink(155, 126, 255, 95)),
            (.55f, Ink(100, 187, 255, 40)), (.78f, Ink(99, 241, 243, 145)), (1f, Ink(71, 171, 255, 65))
        })
            brush.ColorStops.Add(Own(_compositor.CreateColorGradientStop(position, color)));
        var geometry = Own(_compositor.CreateEllipseGeometry());
        geometry.Center = new Vector2(160);
        geometry.Radius = new Vector2(127.4f);
        var rim = Own(_compositor.CreateSpriteShape(geometry));
        rim.StrokeBrush = brush;
        rim.StrokeThickness = 1.5f;
        var visual = Own(_compositor.CreateShapeVisual());
        visual.Size = new Vector2(320);
        visual.Shapes.Add(rim);
        _root.Children.InsertAtTop(visual);
    }

    private SpriteVisual AddGlow(ContainerVisual parent, Vector2 size, Vector2 offset, CompositionBrush brush)
    {
        var visual = Own(_compositor.CreateSpriteVisual());
        visual.Size = size;
        visual.Offset = new Vector3(offset, 0);
        visual.Brush = brush;
        parent.Children.InsertAtTop(visual);
        return visual;
    }

    private CompositionRadialGradientBrush Radial(params (float Position, Color Color)[] stops)
    {
        var brush = Own(_compositor.CreateRadialGradientBrush());
        brush.MappingMode = CompositionMappingMode.Relative;
        brush.EllipseCenter = new Vector2(.5f);
        brush.EllipseRadius = new Vector2(.5f);
        foreach (var (position, color) in stops)
            brush.ColorStops.Add(Own(_compositor.CreateColorGradientStop(position, color)));
        return brush;
    }

    private void AnimateFloat(CompositionObject target, string property, float from, float middle, float to,
        double seconds, bool linear = false)
    {
        var animation = Own(_compositor.CreateScalarKeyFrameAnimation());
        var easing = linear ? Own<CompositionEasingFunction>(_compositor.CreateLinearEasingFunction())
            : Own<CompositionEasingFunction>(_compositor.CreateCubicBezierEasingFunction(new Vector2(.42f, 0), new Vector2(.58f, 1)));
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(.5f, middle, easing);
        animation.InsertKeyFrame(1, to, easing);
        animation.Duration = TimeSpan.FromSeconds(seconds);
        StartLoop(target, property, animation);
    }

    private void AnimateDrift(CompositionObject target, Vector3 from, Vector3 to, double seconds)
    {
        var animation = Own(_compositor.CreateVector3KeyFrameAnimation());
        var easing = Own(_compositor.CreateCubicBezierEasingFunction(new Vector2(.42f, 0), new Vector2(.58f, 1)));
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(.5f, to, easing);
        animation.InsertKeyFrame(1, from, easing);
        animation.Duration = TimeSpan.FromSeconds(seconds);
        StartLoop(target, nameof(Visual.Offset), animation);
    }

    private void StartLoop(CompositionObject target, string property, KeyFrameAnimation animation)
    {
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        target.StartAnimation(property, animation);
        var controller = target.TryGetAnimationController(property)
            ?? throw new InvalidOperationException("The nebula animation could not be controlled.");
        _animations.Add((target, property, Own(controller)));
    }

    private T Own<T>(T resource) where T : IDisposable { _resources.Add(resource); return resource; }
    private static Color Ink(byte r, byte g, byte b, byte a = 255) => new() { R = r, G = g, B = b, A = a };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var surface in _surfaces)
            surface.LoadCompleted -= ImageLoaded;
        _root.ImplicitAnimations = null;
        _root.StopAnimation(nameof(Visual.Scale));
        foreach (var animation in _animations)
            animation.Target.StopAnimation(animation.Property);
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _root.Children.RemoveAll();
        for (var index = _resources.Count - 1; index >= 0; index--)
            _resources[index].Dispose();
        _resources.Clear();
        _animations.Clear();
        _surfaces.Clear();
    }
}
