using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using System.Diagnostics;
using Windows.Foundation;

namespace SymlinkCreator.Controls;

/// <summary>
/// Displays a theme-colored, indeterminate circular arc animation.
/// </summary>
public sealed partial class BusyIndicator : Grid
{
    // Built-in progress controls currently trigger a native startup failure in this
    // unpackaged WinUI deployment, so this indicator uses a lightweight arc instead.
    private const double ArcCenter = 9;
    private const double ArcRadius = 8;
    private const double MinimumSweepDegrees = 45;
    private const double MaximumSweepDegrees = 300;
    private const double RotationDegreesPerSecond = 240;
    private const double SweepCycleSeconds = 1.6;

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(BusyIndicator),
        new PropertyMetadata(false, static (dependencyObject, args) =>
        {
            ((BusyIndicator)dependencyObject).SetActive((bool)args.NewValue);
        }));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(BusyIndicator),
        new PropertyMetadata(null, static (dependencyObject, args) =>
        {
            ((BusyIndicator)dependencyObject)._arcPath.Stroke = (Brush?)args.NewValue;
        }));

    private readonly ArcSegment _arcSegment;
    private readonly DispatcherQueueTimer _animationTimer;
    private readonly XamlPath _arcPath;
    private readonly PathFigure _pathFigure;
    private readonly PathGeometry _pathGeometry;
    private readonly RotateTransform _rotationTransform;
    private readonly Stopwatch _stopwatch = new();
    private bool _isAnimationRunning;

    public BusyIndicator()
    {
        Width = 24;
        Height = 24;

        _arcSegment = new ArcSegment
        {
            Size = new Size(ArcRadius, ArcRadius),
            SweepDirection = SweepDirection.Clockwise
        };
        _pathFigure = new PathFigure { IsClosed = false };
        _pathFigure.Segments.Add(_arcSegment);
        _pathGeometry = new PathGeometry();
        _pathGeometry.Figures.Add(_pathFigure);

        _rotationTransform = new RotateTransform();
        _arcPath = new XamlPath
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = _pathGeometry,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _rotationTransform,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        AutomationProperties.SetAccessibilityView(_arcPath, AccessibilityView.Raw);
        Children.Add(_arcPath);

        _animationTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animationTimer.Tick += AnimationTimer_Tick;
        Loaded += BusyIndicator_Loaded;
        Unloaded += BusyIndicator_Unloaded;

        SetActive(IsActive);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private void SetActive(bool isActive)
    {
        Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        UpdateAnimationState();
    }

    private void BusyIndicator_Loaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateAnimationState();
    }

    private void BusyIndicator_Unloaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        StopAnimation();
    }

    private void UpdateAnimationState()
    {
        if (IsActive && IsLoaded)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void StartAnimation()
    {
        if (_isAnimationRunning)
        {
            return;
        }

        _isAnimationRunning = true;
        _stopwatch.Restart();
        UpdateAnimation();
        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        _isAnimationRunning = false;
        _animationTimer.Stop();
        _stopwatch.Stop();
        _rotationTransform.Angle = 0;
        UpdateArc(MinimumSweepDegrees);
    }

    private void AnimationTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _ = sender;
        _ = args;
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        double sweepPhase = (elapsedSeconds % SweepCycleSeconds) / SweepCycleSeconds;
        double sweepProgress = sweepPhase <= 0.5
            ? sweepPhase * 2
            : (1 - sweepPhase) * 2;
        double easedSweepProgress = sweepProgress * sweepProgress * (3 - (2 * sweepProgress));
        double sweepDegrees = MinimumSweepDegrees +
            ((MaximumSweepDegrees - MinimumSweepDegrees) * easedSweepProgress);

        _rotationTransform.Angle = elapsedSeconds * RotationDegreesPerSecond;
        UpdateArc(sweepDegrees);
    }

    private void UpdateArc(double sweepDegrees)
    {
        _pathFigure.StartPoint = GetPoint(0);
        _arcSegment.Point = GetPoint(sweepDegrees);
        _arcSegment.IsLargeArc = sweepDegrees >= 180;
    }

    private static Point GetPoint(double degrees)
    {
        double radians = (degrees - 90) * Math.PI / 180;
        return new Point(
            ArcCenter + (ArcRadius * Math.Cos(radians)),
            ArcCenter + (ArcRadius * Math.Sin(radians)));
    }
}
