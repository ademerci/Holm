using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Holm.App.Controls;

/// <summary>
/// The buttons in the notch, drawn without a template. The default is the WPF app's NotchButton style: round (small corners in the instrument look),
/// transparent, a faint white disc under the mouse, a stronger one and a slightly smaller icon while pressed,
/// dimmed when disabled. <see cref="Row"/> makes its RowButton (a whole row you can click) and <see cref="Pill"/>
/// its PillButton (Accept / Decline / OK). Never takes focus.
/// </summary>
public sealed class NotchButton : Border
{
    static IBrush White(byte alpha) => Look.InkA(alpha); // the design's ink, white in the dark designs

    static readonly IBrush Hover = White(0x1F), Pressed = White(0x33);
    // The instrument look: a row is a plain line under a hairline, a pill an outline; both stay black at rest.
    // The classic look: a soft panel and a soft pill.
    static readonly IBrush RowRest = Look.Pick<IBrush>(Brushes.Transparent, White(0x14)), RowHover = White(Look.Pick<byte>(0x0D, 0x24)),
        RowPressed = Look.Pick(White(0x1A), White(0x33)), Hairline = White(0x26);
    public static readonly IBrush PillRest = Look.Pick<IBrush>(Brushes.Transparent, White(0x2E));
    static readonly IBrush? PillEdge = Look.Pick<IBrush?>(White(0x4D), null);

    readonly ScaleTransform _pressedScale;
    IBrush _rest = Brushes.Transparent, _hover = Hover, _pressedFill = Pressed;
    /// <summary>A pill keeps its colour and only dims a little under the mouse.</summary>
    bool _pill;
    bool _over, _pressed;

    public event Action? Click;

    public NotchButton(Control content, double pressedScale = 0.86)
    {
        _pressedScale = new ScaleTransform(pressedScale, pressedScale);
        Background = Brushes.Transparent;
        CornerRadius = new CornerRadius(Look.Pick(4, 22));
        Focusable = false;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        content.RenderTransformOrigin = RelativePoint.Center;
        Child = content;
    }

    /// <summary>A whole row you can click: a soft panel that brightens under the mouse, its content on the left.</summary>
    public static NotchButton Row(Control content)
    {
        var row = new NotchButton(content, 0.98)
        {
            _rest = RowRest, _hover = RowHover, _pressedFill = RowPressed,
            Background = RowRest, BorderBrush = Look.Pick<IBrush?>(Hairline, null), BorderThickness = new Thickness(0, Look.Pick(1, 0), 0, 0),
            CornerRadius = new CornerRadius(Look.Pick(0, 14)), Padding = Look.Pick(new Thickness(2, 9, 4, 9), new Thickness(10, 8, 12, 8)),
        };
        content.HorizontalAlignment = HorizontalAlignment.Left;
        return row;
    }

    /// <summary>A small labelled pill; <paramref name="background"/> colours the one that matters (blue for "OK").</summary>
    public static NotchButton Pill(IBrush? background = null)
    {
        var label = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Look.Ink };
        return new NotchButton(label, 0.94)
        {
            _pill = true, _rest = background ?? PillRest,
            Background = background ?? PillRest, BorderBrush = PillEdge, BorderThickness = new Thickness(PillEdge == null ? 0 : 1),
            CornerRadius = new CornerRadius(Look.Pick(3, 13)), Height = 26, Padding = new Thickness(12, 0), MinWidth = 54,
        };
    }

    /// <summary>The text of a <see cref="Pill"/>.</summary>
    public string? Label
    {
        get => (Child as TextBlock)?.Text;
        set { if (Child is TextBlock text) text.Text = value; }
    }

    /// <summary>What the button looks like when nothing touches it: transparent, or lit while its page is open.</summary>
    public IBrush Rest
    {
        get => _rest;
        set { _rest = value; Refresh(); }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEnabledProperty && Child != null) Child.Opacity = IsEnabled ? 1 : 0.3;
    }

    protected override void OnPointerEntered(PointerEventArgs e) { _over = true; Refresh(); }

    protected override void OnPointerExited(PointerEventArgs e) { _over = false; Refresh(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        Refresh();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        Refresh();
        e.Handled = true;
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Click?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pressed = false;
        Refresh();
    }

    void Refresh()
    {
        if (_pill)
        {
            Background = _rest;
            Opacity = _over ? 0.82 : 1;
        }
        else Background = _pressed ? _pressedFill : _over ? _hover : _rest;
        if (Child != null) Child.RenderTransform = _pressed ? _pressedScale : null;
    }
}
