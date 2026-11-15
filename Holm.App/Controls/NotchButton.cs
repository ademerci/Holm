using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Holm.App.Controls;

/// <summary>
/// The buttons in the notch, drawn without a template. The default is the WPF app's NotchButton style: round,
/// transparent, a faint white disc under the mouse, a stronger one and a slightly smaller icon while pressed,
/// dimmed when disabled. <see cref="Row"/> makes its RowButton (a whole row you can click) and <see cref="Pill"/>
/// its PillButton (Accept / Decline / OK). Never takes focus.
/// </summary>
public sealed class NotchButton : Border
{
    static IBrush White(byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF)).ToImmutable();

    static readonly IBrush Hover = White(0x1F), Pressed = White(0x33);
    static readonly IBrush RowRest = White(0x14), RowHover = White(0x24), PillRest = White(0x2E);

    readonly ScaleTransform _pressedScale;
    IBrush _rest = Brushes.Transparent, _hover = Hover;
    /// <summary>A pill keeps its colour and only dims a little under the mouse.</summary>
    bool _pill;
    bool _over, _pressed;

    public event Action? Click;

    public NotchButton(Control content, double pressedScale = 0.86)
    {
        _pressedScale = new ScaleTransform(pressedScale, pressedScale);
        Background = Brushes.Transparent;
        CornerRadius = new CornerRadius(22);
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
            _rest = RowRest, _hover = RowHover,
            Background = RowRest, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 8, 12, 8),
        };
        content.HorizontalAlignment = HorizontalAlignment.Left;
        return row;
    }

    /// <summary>A small labelled pill; <paramref name="background"/> colours the one that matters (blue for "OK").</summary>
    public static NotchButton Pill(IBrush? background = null)
    {
        var label = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
        return new NotchButton(label, 0.94)
        {
            _pill = true, _rest = background ?? PillRest,
            Background = background ?? PillRest, CornerRadius = new CornerRadius(13), Height = 26, Padding = new Thickness(12, 0), MinWidth = 54,
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
        else Background = _pressed ? Pressed : _over ? _hover : _rest;
        if (Child != null) Child.RenderTransform = _pressed ? _pressedScale : null;
    }
}
