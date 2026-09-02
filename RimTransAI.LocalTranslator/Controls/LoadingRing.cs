using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace RimTransAI.LocalTranslator.Controls;

public sealed class LoadingRing : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LoadingRing, bool>(nameof(IsActive));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LoadingRing, double>(nameof(StrokeThickness), 3d);

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<LoadingRing, IBrush?>(nameof(RingBrush));

    private readonly DispatcherTimer _timer;
    private double _angle;
    private bool _isAttached;

    static LoadingRing()
    {
        AffectsRender<LoadingRing>(IsActiveProperty, StrokeThicknessProperty, RingBrushProperty);
    }

    public LoadingRing()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Render, (_, _) =>
        {
            _angle = (_angle + 7) % 360;
            InvalidateVisual();
        });
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty) UpdateTimer();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!IsActive || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var thickness = Math.Max(1, StrokeThickness);
        var radius = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - thickness / 2);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var backgroundPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 90, 105, 120)), thickness);
        var foregroundPen = new Pen(RingBrush ?? new SolidColorBrush(Color.FromRgb(37, 99, 235)), thickness);

        context.DrawEllipse(null, backgroundPen, center, radius, radius);

        var start = (_angle - 90) * Math.PI / 180;
        var sweep = 1.55 * Math.PI;
        var startPoint = new Point(center.X + Math.Cos(start) * radius, center.Y + Math.Sin(start) * radius);
        var end = start + sweep;
        var endPoint = new Point(center.X + Math.Cos(end) * radius, center.Y + Math.Sin(end) * radius);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(startPoint, false);
            geometryContext.ArcTo(endPoint, new Size(radius, radius), 0, true, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, foregroundPen, geometry);
    }

    private void UpdateTimer()
    {
        if (IsActive && _isAttached) _timer.Start();
        else _timer.Stop();
        InvalidateVisual();
    }
}
