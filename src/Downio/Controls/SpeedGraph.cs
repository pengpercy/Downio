using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Downio.Controls;

/// <summary>
/// Scrolling bandwidth chart. <see cref="Value"/> is sampled once a second, but the chart is
/// redrawn every frame and slid by the elapsed fraction of the current second, so the line
/// glides continuously instead of stepping when each sample lands.
/// </summary>
public class SpeedGraph : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SpeedGraph, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SpeedGraph, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<SpeedGraph, int>(nameof(Capacity), 60);

    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    // Floor for the vertical scale so a trickle doesn't fill the whole chart.
    private const double MinScale = 64 * 1024;

    private readonly List<double> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _sampleTimer;
    private TimeSpan _lastSampleAt;
    private TimeSpan _lastFrameAt;
    private double _scale = MinScale;
    private bool _frameRequested;

    static SpeedGraph()
    {
        AffectsRender<SpeedGraph>(StrokeProperty);
    }

    public SpeedGraph()
    {
        _sampleTimer = new DispatcherTimer { Interval = SampleInterval };
        _sampleTimer.Tick += (_, _) => PushSample();
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public int Capacity
    {
        get => GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Start from a flat line spanning the full width, plus the off-screen points scrolling uses.
        _samples.Clear();
        _samples.AddRange(Enumerable.Repeat(0d, Capacity + 2));
        _lastSampleAt = _clock.Elapsed;
        _sampleTimer.Start();
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _sampleTimer.Stop();
    }

    private void PushSample()
    {
        _samples.Add(Math.Max(0, Value));
        while (_samples.Count > Capacity + 2) _samples.RemoveAt(0);
        _lastSampleAt = _clock.Elapsed;
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_frameRequested || !IsEffectivelyVisible) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        _frameRequested = true;
        _lastFrameAt = _clock.Elapsed;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan _)
    {
        _frameRequested = false;
        var now = _clock.Elapsed;
        var dt = Math.Clamp((now - _lastFrameAt).TotalSeconds, 0, 0.1);

        // Ease the scale toward its target so peaks entering or leaving don't make the chart jump.
        var target = TargetScale();
        _scale += (target - _scale) * (1 - Math.Exp(-dt * 5));
        InvalidateVisual();

        // A flat zero line looks the same while scrolling, so idle charts stop asking for frames;
        // the next sample restarts the loop.
        var settled = Math.Abs(_scale - target) < target * 0.002;
        if (settled) _scale = target;
        if (!settled || _samples.Any(v => v > 0))
        {
            RequestFrame();
        }
    }

    private double TargetScale()
    {
        var peak = _samples.Count == 0 ? 0 : _samples.Max() * 1.15;
        if (peak <= MinScale) return MinScale;

        // Round up to 1, 2 or 5 × 10ⁿ so the scale only changes in noticeable steps.
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(peak)));
        foreach (var step in new[] { 1d, 2d, 5d, 10d })
        {
            if (peak <= step * magnitude) return step * magnitude;
        }
        return 10 * magnitude;
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        var count = _samples.Count;
        if (width <= 0 || height <= 0 || count < 2) return;

        var step = width / Math.Max(1, Capacity - 1);
        var progress = Math.Clamp((_clock.Elapsed - _lastSampleAt) / SampleInterval, 0, 1);
        const double strokeThickness = 1.5;
        var plotHeight = height - strokeThickness * 2;

        // The newest sample starts one step past the right edge and slides in over the second.
        var points = new Point[count];
        for (var i = 0; i < count; i++)
        {
            var x = width - (count - 2 - i + progress) * step;
            var y = height - strokeThickness - Math.Min(_samples[i], _scale) / _scale * plotHeight;
            points[i] = new Point(x, y);
        }

        var strokeColor = (Stroke as ISolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x55, strokeColor.R, strokeColor.G, strokeColor.B), 0),
                new GradientStop(Color.FromArgb(0x00, strokeColor.R, strokeColor.G, strokeColor.B), 1)
            }
        };

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, height), true);
            g.LineTo(points[0]);
            AddCurve(g, points);
            g.LineTo(new Point(points[^1].X, height));
            g.EndFigure(true);
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false);
            AddCurve(g, points);
            g.EndFigure(false);
        }

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            context.DrawGeometry(fill, null, area);
            context.DrawGeometry(null, new Pen(Stroke ?? Brushes.DodgerBlue, strokeThickness, lineJoin: PenLineJoin.Round), line);
        }
    }

    // Horizontal-tangent Béziers: smooth between samples without overshooting below zero.
    private static void AddCurve(StreamGeometryContext g, Point[] points)
    {
        for (var i = 1; i < points.Length; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var mid = (to.X - from.X) / 2;
            g.CubicBezierTo(new Point(from.X + mid, from.Y), new Point(to.X - mid, to.Y), to);
        }
    }
}
