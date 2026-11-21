using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App.Controls;

/// <summary>
/// Verity's floor: laid over a row of the open notch (the tool row, or with music a seat beside the song's title), she gets around on its free stretches beside the
/// row's buttons as <see cref="VerityRoam"/> says, a frame at a time while the open notch shows. A click pets her and
/// she leaps; pressed and dragged, she's picked up and flies when let go. A treat falls for her to eat. With animations
/// off she sits still. The lane itself takes no clicks, only she does, so the buttons under it work as before.
/// </summary>
internal sealed class VerityLane : Canvas
{
    const double Size = 30, Floor = 2, Gap = 4;
    /// <summary>How high she may go: up to just under the top of the open notch.</summary>
    const double Headroom = 16;

    static readonly IBrush Cookie = new SolidColorBrush(Color.Parse("#FFD08A3E")).ToImmutable();
    static readonly IBrush Chip = new SolidColorBrush(Color.Parse("#FF5A3418")).ToImmutable();

    readonly DispatcherTimer _frame = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly VerityRoam _roam = new(480, Size, new Random(), Headroom);
    readonly List<(double From, double To)> _free = new();
    readonly TreatView _treat = new() { Width = TreatView.Size, Height = TreatView.Size, IsVisible = false, IsHitTestVisible = false };
    DateTime _last;
    bool _active, _animate = true;
    Point? _pressAt;
    (Point At, DateTime Time) _dragLast;
    Vector _dragSpeed;

    public VerityFace Face { get; } = new() { Width = Size, Height = Size, Cursor = new Cursor(StandardCursorType.Hand) };

    /// <summary>A click on her.</summary>
    public event Action? Petted;
    /// <summary>She finished a treat.</summary>
    public event Action? Ate;

    public VerityLane()
    {
        MinHeight = Size + Floor;
        ZIndex = 2; // over the row's buttons while she flies past them
        ClipToBounds = false; // her leaps go up to the top of the notch
        Children.Add(_treat); // behind her
        Children.Add(Face);
        ToolTip.SetTip(Face, "Verity: click to pet her, drag to throw her, right-click to feed her");
        Face.PointerPressed += OnPressed;
        Face.PointerMoved += OnMoved;
        Face.PointerReleased += OnReleased;
        Face.PointerCaptureLost += (_, _) => { if (_roam.Held) _roam.Throw(0, 0); _pressAt = null; };
        _roam.Ate += () => Ate?.Invoke();
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

    /// <summary>A moment: a leap for joy, or a start.</summary>
    public void React(VerityCue cue)
    {
        Face.Express(cue);
        if (_active && _animate) _roam.Jump();
    }

    /// <summary>A treat drops a little beside her.</summary>
    public void Feed()
    {
        // Sitting still (animations off), she has it at once.
        if (!_active || !_animate) { Ate?.Invoke(); return; }
        _roam.Feed(_roam.X + (_roam.X > Bounds.Width / 2 ? -1 : 1) * (20 + Random.Shared.NextDouble() * 30));
    }

    /// <summary>The mouse over the open notch, in this lane's coordinates, or null when it left.</summary>
    public void Notice(double? x) => _roam.Notice(x);

    void Schedule()
    {
        bool run = _active && _animate;
        if (run && !_frame.IsEnabled) { _last = DateTime.UtcNow; _frame.Start(); }
        else if (!run && _frame.IsEnabled) _frame.Stop();
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        Tick((now - _last).TotalSeconds);
        _last = now;
    }

    /// <summary>One frame of her life, <paramref name="seconds"/> after the last one.</summary>
    internal void Tick(double seconds)
    {
        if (Bounds.Width > 0) _roam.SetFloor(Bounds.Width, FreeStretches());
        _roam.Step(seconds, Mood);
        Face.Chewing = _roam.Eating;
        Place();
        PlaceTreat();
    }

    /// <summary>The stretches of the row no button stands on, with a little room beside each button.</summary>
    List<(double From, double To)> FreeStretches()
    {
        _free.Clear();
        if (Parent is not Panel row) { _free.Add((0, Bounds.Width)); return _free; }
        var taken = row.Children
            .Where(c => c != this && c.IsVisible && c.Bounds.Width > 0 && c.Bounds.Bottom > Bounds.Top + 1 && c.Bounds.Top < Bounds.Bottom - 1)
            .Select(c => (From: c.Bounds.Left - Bounds.Left - Gap, To: c.Bounds.Right - Bounds.Left + Gap))
            .OrderBy(span => span.From);
        double from = 0;
        foreach (var span in taken)
        {
            if (span.From > from) _free.Add((from, span.From));
            from = Math.Max(from, span.To);
        }
        if (from < Bounds.Width) _free.Add((from, Bounds.Width));
        return _free;
    }

    double FloorY => (Bounds.Height > 0 ? Bounds.Height : MinHeight) - Floor;

    void Place()
    {
        SetLeft(Face, _roam.X - Size / 2);
        SetTop(Face, FloorY - Size - _roam.Lift);
        Face.Pose(_roam.Squash, _roam.Angle, _roam.Gaze);
    }

    void PlaceTreat()
    {
        _treat.IsVisible = _roam.Treat != null;
        if (_roam.Treat is not { } treat) return;
        _treat.Left = Math.Max(0.15, _roam.TreatLeft);
        SetLeft(_treat, treat.X - TreatView.Size / 2);
        SetTop(_treat, FloorY - TreatView.Size - treat.Height);
        _treat.InvalidateVisual();
    }

    /// <summary>A round cookie with chocolate chips; it gets smaller as she eats it.</summary>
    sealed class TreatView : Control
    {
        public const double Size = 9;
        public double Left = 1;

        public override void Render(DrawingContext context)
        {
            double r = Size / 2 * Left;
            var c = new Point(Size / 2, Size - r);
            context.DrawEllipse(Cookie, null, c, r, r);
            foreach (var (dx, dy) in new[] { (-0.4, -0.3), (0.35, -0.1), (-0.05, 0.4) })
                context.DrawEllipse(Chip, null, new Point(c.X + dx * r, c.Y + dy * r), r * 0.2, r * 0.2);
        }
    }

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Face).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        _pressAt = e.GetPosition(this);
        _dragLast = (_pressAt.Value, DateTime.UtcNow);
        _dragSpeed = default;
        e.Pointer.Capture(Face);
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressAt is not { } start) return;
        var at = e.GetPosition(this);
        if (!_roam.Held)
        {
            if (Math.Abs(at.X - start.X) < 4 && Math.Abs(at.Y - start.Y) < 4) return;
            _roam.Grab();
            Schedule();
        }
        var now = DateTime.UtcNow;
        double dt = (now - _dragLast.Time).TotalSeconds;
        if (dt > 0.005) _dragSpeed = _dragSpeed * 0.5 + (at - _dragLast.At) / dt * 0.5;
        _dragLast = (at, now);
        // The pointer holds her by her middle.
        _roam.HoldAt(at.X, FloorY - at.Y - Size * 0.41);
        Place();
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressAt == null) return;
        _pressAt = null;
        e.Handled = true;
        if (_roam.Held)
        {
            // A pause before letting go drops her instead.
            bool stale = (DateTime.UtcNow - _dragLast.Time).TotalSeconds > 0.08;
            _roam.Throw(stale ? 0 : _dragSpeed.X, stale ? 0 : -_dragSpeed.Y);
        }
        else
        {
            _roam.Jump();
            Petted?.Invoke();
        }
        e.Pointer.Capture(null);
    }
}
