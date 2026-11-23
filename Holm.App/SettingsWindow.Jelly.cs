using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

sealed partial class SettingsWindow
{
    readonly ScaleTransform _jellyScale = new(1, 1);
    readonly SkewTransform _jellySkew = new();
    SpringValue _jellyLean = new(0, 0.01, 0.05), _jellySquash = new(0, 0.01, 0.05);
    PixelPoint? _jellyPosition;
    long _jellyFrame;
    INotchWindowPlatform _jellyPlatform = null!;
    IDisposable? _jellyTiming;

    void InitJellyMotion(INotchWindowPlatform platform)
    {
        _jellyPlatform = platform;
        _surface.RenderTransform = new TransformGroup { Children = { _jellySkew, _jellyScale } };
        // Start after the click is delivered so a button doesn't move away from the pressed pointer.
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            double lean = Math.Clamp(e.GetPosition(this).X / Math.Max(1, ClientSize.Width) - 0.5, -0.5, 0.5);
            WobbleSettings(lean, 1);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        Activated += (_, _) => _jellyPosition = Position;
        PositionChanged += (_, e) =>
        {
            if (IsActive && _jellyPosition is { } previous)
            {
                double x = (e.Point.X - previous.X) / RenderScaling;
                double y = (e.Point.Y - previous.Y) / RenderScaling;
                // Sideways drags stretch the surface too, even when the pointer stays on the same row.
                WobbleSettings(Math.Clamp(-x / 8, -1, 1), Math.Clamp((y + Math.Abs(x) * 0.25) / 10, -1, 1));
            }
            _jellyPosition = e.Point;
        };
        Closed += (_, _) => StopJellyFrames();
    }

    void WobbleSettings(double lean, double squash)
    {
        if (!Spring.Jelly || !_animations() || _dialog.IsVisible || !IsVisible || (lean == 0 && squash == 0)) return;
        double leanVelocity = Math.Clamp(_jellyLean.Velocity + lean * 25, -35, 35);
        double squashVelocity = Math.Clamp(_jellySquash.Velocity + squash * 24, -34, 34);
        _jellyLean.Kick(leanVelocity - _jellyLean.Velocity, 0.34, 0.65);
        _jellySquash.Kick(squashVelocity - _jellySquash.Velocity, 0.29, 0.65);
        if (_jellyFrame != 0) return;
        _jellyTiming = _jellyPlatform.BeginAnimationTiming();
        _jellyFrame = Stopwatch.GetTimestamp();
        RequestAnimationFrame(OnJellyFrame);
    }

    void OnJellyFrame(TimeSpan _)
    {
        if (_jellyFrame == 0) return;
        if (!Spring.Jelly || !_animations() || _dialog.IsVisible || !IsVisible)
        {
            _jellyLean.Snap(0);
            _jellySquash.Snap(0);
        }
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp((double)(now - _jellyFrame) / Stopwatch.Frequency, 0.001, 0.05);
        bool settled = _jellyLean.Step(dt) & _jellySquash.Step(dt);
        double squash = Math.Tanh(_jellySquash.Value) * 0.15;
        double angle = Math.Tanh(_jellyLean.Value) * 9;
        _jellySkew.AngleX = angle;
        // Skew widens the outline. Fit it inside the native window so the edge and buttons stay visible.
        double skewWidth = Math.Abs(Math.Tan(angle * Math.PI / 180)) * _surface.Bounds.Height;
        double width = Math.Max(1, _surface.Bounds.Width);
        _jellyScale.ScaleX = (1 + Math.Min(0, squash)) * width / (width + skewWidth);
        _jellyScale.ScaleY = 1 - Math.Max(0, squash);
        if (settled) StopJellyFrames();
        else
        {
            _jellyFrame = now;
            RequestAnimationFrame(OnJellyFrame);
        }
    }

    void StopJellyFrames()
    {
        _jellyFrame = 0;
        _jellyTiming?.Dispose();
        _jellyTiming = null;
    }
}
