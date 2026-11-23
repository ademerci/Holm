using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App.Controls;

/// <summary>
/// Verity's floor along the bottom of the open notch: she gets around on it as <see cref="VerityRoam"/> says, a
/// frame at a time while the open notch shows, and leaps when you click her. With animations off she sits still.
/// </summary>
internal sealed class VerityLane : Canvas
{
    const double Size = 30, Floor = 2;

    readonly DispatcherTimer _frame = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly VerityRoam _roam = new(480, Size, new Random());
    DateTime _last;
    bool _active, _animate = true;

    public VerityFace Face { get; } = new() { Width = Size, Height = Size, Cursor = new Cursor(StandardCursorType.Hand) };

    /// <summary>A click on her.</summary>
    public event Action? Petted;

    public VerityLane()
    {
        Height = Size + Floor;
        ClipToBounds = false; // her leaps go up over the row above
        Children.Add(Face);
        ToolTip.SetTip(Face, "Pet Verity");
        Face.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _roam.Jump();
            Petted?.Invoke();
        };
        _frame.Tick += (_, _) => Tick();
        Place();
    }

    public VerityMood Mood { get; set; }

    /// <summary>The open notch shows: she moves.</summary>
    public bool Active
    {
        get => _active;
        set { _active = value; Face.Active = value; Schedule(); }
    }

    public bool Animate
    {
        get => _animate;
        set { _animate = value; Face.Animate = value; Schedule(); }
    }

    void Schedule()
    {
        bool run = _active && _animate;
        if (run && !_frame.IsEnabled) { _last = DateTime.UtcNow; _frame.Start(); }
        else if (!run && _frame.IsEnabled) _frame.Stop();
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        if (Bounds.Width > 0) _roam.Resize(Bounds.Width);
        _roam.Step((now - _last).TotalSeconds, Mood);
        _last = now;
        Place();
    }

    void Place()
    {
        SetLeft(Face, _roam.X - Size / 2);
        SetTop(Face, Height - Floor - Size - _roam.Lift);
        Face.Pose(_roam.Squash, _roam.Angle, _roam.Moving ? _roam.Facing : 0);
    }
}
