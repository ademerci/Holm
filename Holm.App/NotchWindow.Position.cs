using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Drag the notch along the top edge of the chosen display (the WPF app's MainWindow.Position.cs), persist its
/// display-relative position, and reset it from Settings.
/// </summary>
public sealed partial class NotchWindow
{
    const double DragThreshold = 4;

    bool _positionHeld, _positionMoved;
    IPointer? _positionPointer;
    int _positionStartX;
    double? _positionBeforeDrag;
    double _positionStartFraction;

    void InitPositionDrag()
    {
        AddHandler(PointerPressedEvent, PositionDragDown, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, PositionDragMove, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, PositionDragUp, RoutingStrategies.Tunnel);
        _island.PointerCaptureLost += (_, _) => FinishPositionDrag();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!_positionHeld || e.Key != Key.Escape) return;
            _settings.NotchPosition = _positionBeforeDrag;
            _positionMoved = false;
            FinishPositionDrag();
            PositionWindow();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    void PositionDragDown(object? sender, PointerPressedEventArgs e)
    {
        if (!_settings.Draggable || !_island.IsPointerOver || _menuOpen || !_opened) return;
        if (!e.GetCurrentPoint(_island).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(_island);
        if (point.Y < 0 || point.Y > 8) return;
        e.Pointer.Capture(_island);
        if (!Equals(e.Pointer.Captured, _island)) return;
        _positionPointer = e.Pointer;
        _positionHeld = true;
        _positionMoved = false;
        _positionStartX = ScreenX(e);
        _positionBeforeDrag = _settings.NotchPosition;
        _positionStartFraction = _settings.NotchPosition is { } fraction && double.IsFinite(fraction) ? fraction : 0.5;
        _expandDelay.Stop();
        _collapseDelay.Stop();
        SetHoverBoost(false);
    }

    int ScreenX(PointerEventArgs e) => Position.X + (int)Math.Round(e.GetPosition(this).X * RenderScaling);

    void PositionDragMove(object? sender, PointerEventArgs e)
    {
        if (!_positionHeld) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { FinishPositionDrag(); return; }
        var screen = ChosenScreen();
        if (screen == null) { FinishPositionDrag(); return; }
        int delta = ScreenX(e) - _positionStartX;
        if (!_positionMoved && Math.Abs(delta) < DragThreshold * screen.Scaling) return;
        int width = (int)Math.Round(Width * screen.Scaling);
        int start = NotchPlacement.Left(screen.Bounds.X, screen.Bounds.Right, width, _positionStartFraction);
        int x = Math.Clamp(start + delta, screen.Bounds.X, Math.Max(screen.Bounds.X, screen.Bounds.Right - width));
        _settings.NotchPosition = NotchPlacement.Fraction(screen.Bounds.X, screen.Bounds.Right, width, x);
        _positionMoved = true;
        PositionWindow();
        e.Handled = true;
    }

    void PositionDragUp(object? sender, PointerReleasedEventArgs e)
    {
        if (!_positionHeld) return;
        bool moved = _positionMoved;
        FinishPositionDrag();
        if (moved) e.Handled = true;
    }

    void FinishPositionDrag()
    {
        if (!_positionHeld) return;
        _positionHeld = false;
        if (_positionPointer != null && Equals(_positionPointer.Captured, _island)) _positionPointer.Capture(null);
        _positionPointer = null;
        if (_positionMoved) _settings.Save();
        _positionMoved = false;
        if (_hovered && !PinnedClockOpen && !_island.IsPointerOver) _collapseDelay.Start();
    }

    void ResetNotchPosition()
    {
        FinishPositionDrag();
        _settings.NotchPosition = null;
        _settings.Save();
        PositionWindow();
    }
}
