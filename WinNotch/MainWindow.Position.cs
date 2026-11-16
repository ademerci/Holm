using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinNotch;

internal static class NotchPlacement
{
    internal static int Left(int left, int right, int width, double? position)
    {
        double fraction = position is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
        int x = (int)Math.Round(left + (right - left) * fraction - width / 2.0);
        return Math.Clamp(x, left, Math.Max(left, right - width));
    }

    internal static double Fraction(int left, int right, int width, int x)
        => Math.Clamp((x + width / 2.0 - left) / Math.Max(1, right - left), 0, 1);
}

public partial class MainWindow
{
    bool _positionHeld, _positionMoved;
    int _positionStartX;
    double? _positionBeforeDrag;
    double _positionStartFraction;

    void InitPositionDrag()
    {
        PreviewMouseLeftButtonDown += PositionDragDown;
        PreviewMouseMove += PositionDragMove;
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_positionHeld) return;
            bool moved = _positionMoved;
            FinishPositionDrag();
            if (moved) e.Handled = true;
        };
        Island.LostMouseCapture += (_, _) => FinishPositionDrag();
        PreviewKeyDown += (_, e) =>
        {
            if (!_positionHeld || e.Key != Key.Escape) return;
            _settings.NotchPosition = _positionBeforeDrag;
            _positionMoved = false;
            FinishPositionDrag();
            PositionWindow();
            e.Handled = true;
        };
    }

    void PositionDragDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.Draggable || !Island.IsMouseOver || MenuOpen || _hwnd == IntPtr.Zero) return;
        var point = e.GetPosition(Island);
        if (point.Y < 0 || point.Y > 8 || !Native.GetCursorPos(out var cursor)) return;
        _positionHeld = Island.CaptureMouse();
        if (!_positionHeld) return;
        _positionMoved = false;
        _positionStartX = cursor.X;
        _positionBeforeDrag = _settings.NotchPosition;
        _positionStartFraction = _settings.NotchPosition is { } fraction && double.IsFinite(fraction) ? fraction : 0.5;
        _expandDelay.Stop();
        _collapseDelay.Stop();
        SetHoverBoost(false);
    }

    void PositionDragMove(object sender, MouseEventArgs e)
    {
        if (!_positionHeld) return;
        if (e.LeftButton != MouseButtonState.Pressed) { FinishPositionDrag(); return; }
        if (!Native.GetCursorPos(out var cursor)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int delta = cursor.X - _positionStartX;
        if (!_positionMoved && Math.Abs(delta) < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX) return;
        var screen = Native.Screens().Find(s => s.Handle == _monitor);
        if (screen == null) { FinishPositionDrag(); return; }
        int width = (int)Math.Round(Width * dpi.DpiScaleX);
        int start = NotchPlacement.Left(screen.Bounds.Left, screen.Bounds.Right, width, _positionStartFraction);
        int x = Math.Clamp(start + delta, screen.Bounds.Left, Math.Max(screen.Bounds.Left, screen.Bounds.Right - width));
        _settings.NotchPosition = NotchPlacement.Fraction(screen.Bounds.Left, screen.Bounds.Right, width, x);
        _positionMoved = true;
        PositionWindow();
        e.Handled = true;
    }

    void FinishPositionDrag()
    {
        if (!_positionHeld) return;
        _positionHeld = false;
        if (Island.IsMouseCaptured) Island.ReleaseMouseCapture();
        if (_positionMoved) _settings.Save();
        _positionMoved = false;
        if (_expanded && !PinnedClockOpen && !Island.IsMouseOver) _collapseDelay.Start();
    }

    void MenuDraggable_Click(object sender, RoutedEventArgs e)
    {
        FinishPositionDrag();
        _settings.Draggable = MenuDraggable.IsChecked;
        _settings.Save();
    }

    void ResetNotchPosition()
    {
        FinishPositionDrag();
        _settings.NotchPosition = null;
        _settings.Save();
        PositionWindow();
    }
}
