using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using WinNotch;

namespace Holm.App;

sealed partial class SettingsWindow
{
    readonly ScaleTransform _jellyScale = new(1, 1);
    readonly SkewTransform _jellySkew = new();
    SpringValue _jellyLean = new(0, 0.01, 0.05), _jellySquash = new(0, 0.01, 0.05);
    PixelPoint? _jellyPosition;
    long _jellyFrame;

    void InitJellyMotion()
    {
        _surface.RenderTransform = new TransformGroup { Children = { _jellySkew, _jellyScale } };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) WobbleSettings(0, 0.7);
        }, RoutingStrategies.Tunnel);
        Activated += (_, _) => _jellyPosition = Position;
        PositionChanged += (_, e) =>
        {
            if (IsActive && _jellyPosition is { } previous)
            {
                double x = (e.Point.X - previous.X) / RenderScaling;
                double y = (e.Point.Y - previous.Y) / RenderScaling;
                WobbleSettings(Math.Clamp(-x / 14, -1, 1), Math.Clamp(y / 22, -1, 1));
            }
            _jellyPosition = e.Point;
        };
    }

    void WobbleSettings(double lean, double squash)
    {
        if (!Spring.Jelly || !_animations() || _dialog.IsVisible || !IsVisible || (lean == 0 && squash == 0)) return;
        double leanVelocity = Math.Clamp(_jellyLean.Velocity + lean * 18, -25, 25);
        double squashVelocity = Math.Clamp(_jellySquash.Velocity + squash * 20, -28, 28);
        _jellyLean.Kick(leanVelocity - _jellyLean.Velocity, 0.24, 0.65);
        _jellySquash.Kick(squashVelocity - _jellySquash.Velocity, 0.23, 0.65);
        if (_jellyFrame != 0) return;
        _jellyFrame = Stopwatch.GetTimestamp();
        RequestAnimationFrame(OnJellyFrame);
    }

    void OnJellyFrame(TimeSpan _)
    {
        if (!Spring.Jelly || !_animations() || _dialog.IsVisible)
        {
            _jellyLean.Snap(0);
            _jellySquash.Snap(0);
        }
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp((double)(now - _jellyFrame) / Stopwatch.Frequency, 0.001, 0.05);
        bool settled = _jellyLean.Step(dt) & _jellySquash.Step(dt);
        double stretch = Math.Clamp(_jellySquash.Value, -1.2, 1.2);
        _jellySkew.AngleX = Math.Clamp(_jellyLean.Value, -1.3, 1.3);
        _jellyScale.ScaleX = 1 + stretch * 0.012;
        _jellyScale.ScaleY = 1 - stretch * 0.016;
        _jellyFrame = settled ? 0 : now;
        if (!settled) RequestAnimationFrame(OnJellyFrame);
    }
}
