using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinNotch;

/// <summary>iOS-style battery: outline, proportional fill, green while plugged in, red when low.</summary>
public sealed class BatteryGlyph : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(BatteryGlyph), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(
        nameof(Charging), typeof(bool), typeof(BatteryGlyph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }

    static readonly Brush Outline = Freeze(new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF)));
    static readonly Pen OutlinePen = Freeze(new Pen(Outline, 1));
    static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)));
    static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)));
    static readonly Pen BoltPen = Freeze(new Pen(Brushes.Black, 0.9) { LineJoin = PenLineJoin.Round });
    static readonly Geometry Bolt = Freeze(Geometry.Parse("M12.6,1.6 L8.4,6.6 L11,6.6 L9.6,10.4 L13.8,5.4 L11.2,5.4 Z"));

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override Size MeasureOverride(Size availableSize) => new(25, 12);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(null, OutlinePen, new Rect(0.5, 0.5, 21, 11), 3.2, 3.2);
        dc.DrawRoundedRectangle(Outline, null, new Rect(22.6, 4, 1.8, 4), 0.9, 0.9);

        double level = Math.Clamp(Level, 0, 100);
        var fill = Charging ? Green : level <= 20 ? Red : Brushes.White;
        dc.DrawRoundedRectangle(fill, null, new Rect(2, 2, Math.Max(2, 18 * level / 100), 8), 1.6, 1.6);

        if (Charging)
        {
            dc.DrawGeometry(Brushes.White, BoltPen, Bolt);
        }
    }
}

/// <summary>Thin progress / volume bar that thickens on hover and can be dragged.</summary>
public sealed class MiniSlider : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MiniSlider), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MiniSlider), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public bool IsInteractive { get; set; } = true;
    public bool IsDragging { get; private set; }

    /// <summary>Raised continuously while dragging.</summary>
    public event Action<double>? ValueChanging;
    /// <summary>Raised once when the drag ends.</summary>
    public event Action<double>? ValueCommitted;

    static readonly Brush Track;
    bool _hover;

    static MiniSlider()
    {
        Track = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        Track.Freeze();
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, 16);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // whole strip is clickable

        double thickness = IsInteractive && (_hover || IsDragging) ? 7 : 4;
        double y = (h - thickness) / 2, radius = thickness / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, y, w, thickness), radius, radius);

        double filled = Math.Clamp(Value, 0, 1) * w;
        if (filled > 0.5)
            dc.DrawRoundedRectangle(Fill, null, new Rect(0, y, Math.Max(filled, thickness), thickness), radius, radius);
    }

    protected override void OnMouseEnter(MouseEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = false; InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsInteractive) return;
        IsDragging = true;
        CaptureMouse();
        MoveTo(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsDragging) MoveTo(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsDragging) return;
        IsDragging = false;
        ReleaseMouseCapture();
        ValueCommitted?.Invoke(Value);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (!IsDragging) return;
        IsDragging = false;
        ValueCommitted?.Invoke(Value);
        InvalidateVisual();
    }

    void MoveTo(MouseEventArgs e)
    {
        if (ActualWidth <= 0) return;
        Value = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);
        ValueChanging?.Invoke(Value);
    }
}
