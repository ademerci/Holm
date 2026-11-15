using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Holm.App.Controls;

/// <summary>
/// A thin bar that fills from the left: the song's progress, a volume. Drawn directly, without a template, like
/// the WPF app's MiniSlider (Controls.cs): 4 px thick, 7 px under the mouse or while dragging.
/// </summary>
public sealed class MiniSlider : Control
{
    static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)).ToImmutable();

    double _value;
    IBrush _fill = Brushes.White;
    bool _interactive = true, _hover;

    /// <summary>0 to 1.</summary>
    public double Value
    {
        get => _value;
        set { if (_value != value) { _value = value; InvalidateVisual(); } }
    }

    public IBrush Fill
    {
        get => _fill;
        set { _fill = value; InvalidateVisual(); }
    }

    /// <summary>Whether the mouse can move it.</summary>
    public bool IsInteractive
    {
        get => _interactive;
        set { _interactive = value; InvalidateVisual(); }
    }

    public bool IsDragging { get; private set; }

    /// <summary>Raised continuously while dragging.</summary>
    public event Action<double>? ValueChanging;
    /// <summary>Raised once when the drag ends.</summary>
    public event Action<double>? ValueCommitted;

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, 16);

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // whole strip is clickable

        double thickness = IsInteractive && (_hover || IsDragging) ? 7 : 4;
        double y = (h - thickness) / 2, radius = thickness / 2;
        dc.DrawRectangle(Track, null, new Rect(0, y, w, thickness), radius, radius);

        double filled = Math.Clamp(Value, 0, 1) * w;
        if (filled > 0.5)
            dc.DrawRectangle(Fill, null, new Rect(0, y, Math.Max(filled, thickness), thickness), radius, radius);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { _hover = false; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!IsInteractive || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        IsDragging = true;
        e.Pointer.Capture(this);
        MoveTo(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (IsDragging) MoveTo(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!IsDragging) return;
        IsDragging = false;
        e.Pointer.Capture(null);
        ValueCommitted?.Invoke(Value);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (!IsDragging) return;
        IsDragging = false;
        ValueCommitted?.Invoke(Value);
        InvalidateVisual();
    }

    void MoveTo(PointerEventArgs e)
    {
        if (Bounds.Width <= 0) return;
        Value = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        ValueChanging?.Invoke(Value);
    }
}
