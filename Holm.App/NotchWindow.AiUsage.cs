using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Codenotch's ring (github.com/vinzdg/codenotch, MIT), as the WPF app draws it: the tool's mark in a track whose arc
/// fills with the usage and turns green → yellow → red as it runs out, and a thin inner arc that circles while an agent
/// is at work.
/// </summary>
internal sealed class UsageRing
{
    static IBrush B(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b)).ToImmutable();

    public static readonly IBrush Ample = B(0x00, 0xFF, 0x88), Watch = B(0xF2, 0xFF, 0x00), Critical = B(0xFF, 0x3F, 0x00);
    static readonly IBrush Track = B(0x30, 0x30, 0x30);

    /// <summary>Codenotch's default thresholds: watch from 50 %, critical from 70 %.</summary>
    public static IBrush ColorOf(double? percent) => percent >= 70 ? Critical : percent >= 50 ? Watch : Ample;
    public static string NameOf(AiTool tool) => tool == AiTool.ClaudeCode ? "Claude" : "Codex";

    public readonly Grid Root;
    public AiUsageSnapshot? Shown { get; private set; }
    public bool Working { get; private set; }

    readonly double _size, _radius, _stroke;
    readonly Path _arc, _activity;
    readonly Border _mark = new();
    AiTool? _markFor;
    bool _animate;
    CancellationTokenSource? _spin;

    public UsageRing(double size)
    {
        _size = size;
        _stroke = Math.Max(1.8, size / 11);
        _radius = (size - _stroke) / 2;
        var track = new Ellipse
        {
            Width = size - _stroke, Height = size - _stroke, Stroke = Track, StrokeThickness = _stroke,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _arc = new Path { StrokeThickness = _stroke, StrokeLineCap = PenLineCap.Round, Width = size, Height = size };
        double inner = _radius - _stroke - 0.8;
        _activity = new Path
        {
            Data = Arc(size / 2, inner, 22), Stroke = Look.Ink, StrokeThickness = Math.Max(1, _stroke * 0.55), StrokeLineCap = PenLineCap.Round,
            RenderTransform = new RotateTransform(), RenderTransformOrigin = RelativePoint.Center, Width = size, Height = size, IsVisible = false,
        };
        _mark.HorizontalAlignment = HorizontalAlignment.Center;
        _mark.VerticalAlignment = VerticalAlignment.Center;
        Root = new Grid { Width = size, Height = size, Children = { track, _arc, _activity, _mark } };
    }

    public void Update(AiUsageSnapshot usage, bool animate)
    {
        Shown = usage;
        if (_markFor != usage.Tool)
        {
            _markFor = usage.Tool;
            _mark.Child = AiMarks.For(usage.Tool, Math.Round(_size * 0.46), Look.Ink);
        }
        _arc.Stroke = ColorOf(usage.Percent);
        _arc.Data = Arc(_size / 2, _radius, Math.Clamp(usage.Percent ?? 0, 0, 100));
        _arc.Opacity = usage.IsStale ? 0.45 : 1; // an old reading is dimmed, as Codenotch does
        SetWorking(usage.Active.Count > 0, animate);
    }

    /// <summary>The inner arc circles while a session works; it rests (and costs nothing) otherwise.</summary>
    void SetWorking(bool working, bool animate)
    {
        if (working == Working && animate == _animate) return;
        (Working, _animate) = (working, animate);
        _activity.IsVisible = working;
        _spin?.Cancel();
        _spin = null;
        if (!working || !animate) { ((RotateTransform)_activity.RenderTransform!).Angle = 0; return; }
        var turn = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.4), IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 360d) } },
            },
        };
        _spin = new CancellationTokenSource();
        _ = turn.RunAsync(_activity, _spin.Token);
    }

    /// <summary>The arc from twelve o'clock, clockwise.</summary>
    internal static Geometry? Arc(double c, double r, double percent)
    {
        if (percent < 0.5) return null;
        if (percent >= 99.5) return new EllipseGeometry { Center = new Point(c, c), RadiusX = r, RadiusY = r };
        double angle = percent / 100 * 2 * Math.PI;
        var arc = new StreamGeometry();
        using var ctx = arc.Open();
        ctx.BeginFigure(new Point(c, c - r), false);
        ctx.ArcTo(new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)), new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise);
        ctx.EndFigure(false);
        return arc;
    }
}

// AI usage, Codenotch's way inside Holm (the WPF app's MainWindow.AiUsage.cs). Closed, the notch widens and carries a
// ring at each end: Claude on the left, Codex on the right (a single tool sits on the right). Open, the AI tool shows
// every limit with its reset and the sessions at work. Claude's numbers are the real ones from Anthropic
// (ClaudeLimits.cs), with an estimate from the logs only when there is no sign-in; Codex's come from its own logs
// (AiUsage.cs). Nothing is shared or sent elsewhere.
public sealed partial class NotchWindow
{
    internal const double AiWing = 48;
    const double AiRingSize = 18;

    IReadOnlyList<AiUsageSnapshot> _aiUsage = Array.Empty<AiUsageSnapshot>();
    AiUsageReader? _aiClaude, _aiCodex;
    ClaudeLimits? _claudeLimits;
    DispatcherTimer? _aiTimer;
    bool _aiReading, _aiRingHot;
    Grid? _aiWings;
    (StackPanel Panel, UsageRing Ring, TextBlock Number)? _aiLeft, _aiRight;
    readonly Dictionary<Control, Thickness> _wingBase = new();
    internal (bool Left, bool Right) AiWingsShown { get; private set; }

    readonly StackPanel _aiPage = new() { IsVisible = false };
    NotchButton _aiToolBtn = null!;

    void StartAiUsage()
    {
        _aiTimer?.Stop();
        if (!_settings.AiUsage)
        {
            ShowAiUsage(Array.Empty<AiUsageSnapshot>());
            return;
        }
        if (_aiTimer == null)
        {
            _aiTimer = new DispatcherTimer();
            _aiTimer.Tick += (_, _) => _ = RefreshAiUsageAsync();
        }
        _aiTimer.Interval = TimeSpan.FromSeconds(8); // the first look waits until start-up is over
        _aiTimer.Start();
    }

    void StopAiUsage() => _aiTimer?.Stop();

    async Task RefreshAiUsageAsync()
    {
        if (_aiReading || !_settings.AiUsage || _aiTimer == null) return;
        _aiTimer.Interval = TimeSpan.FromMinutes(1);
        if (_hiddenForFullscreen) return; // nothing shows over a game: look again when it's over
        _aiReading = true;
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string claudeDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c : System.IO.Path.Combine(home, ".claude");
            var claude = _aiClaude ??= new AiUsageReader(AiTool.ClaudeCode, claudeDir);
            var codex = _aiCodex ??= new AiUsageReader(AiTool.Codex, Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } x ? x : System.IO.Path.Combine(home, ".codex"));
            var limits = _claudeLimits ??= new ClaudeLimits(claudeDir);
            var now = DateTime.UtcNow;
            var found = await Task.Run(async () =>
            {
                await limits.RefreshAsync(now); // at most every five minutes; in between the last reading stays
                return new[] { WithLiveLimits(claude.Read(now), limits, now), codex.Read(now) };
            });
            if (!_settings.AiUsage) return;
            var usage = found.Where(u => u != null).Select(u => u!).ToList();
            ShowAiUsage(usage);
            // Numbers only: what the agents work on, and the sign-in, stay out of the log.
            foreach (var u in usage) Diag.Log(string.Create(CultureInfo.InvariantCulture, $"AI usage: {u.Tool} {u.Percent:0} % ({(u.IsLimit ? "reported" : "estimate")}{(u.IsStale ? ", old" : "")}), {u.Active.Count} working"));
            if (limits.Problem != null) Diag.Log("AI usage: Claude limits: " + limits.Problem);
            if (usage.Count == 0) _aiTimer.Interval = TimeSpan.FromMinutes(15); // neither tool here: look now and then
        }
        catch (Exception ex) { Diag.Log("AI usage: " + ex.GetType().Name + ": " + ex.Message); }
        finally { _aiReading = false; }
    }

    /// <summary>Claude's snapshot with the real limits in place of the log estimate, when there are any.</summary>
    internal static AiUsageSnapshot? WithLiveLimits(AiUsageSnapshot? logs, ClaudeLimits limits, DateTime nowUtc)
        => WithLiveLimits(logs, limits.Limits, limits.FetchedAt, limits.IsStale(nowUtc), limits.Problem);

    internal static AiUsageSnapshot? WithLiveLimits(AiUsageSnapshot? logs, IReadOnlyList<AiUsageLimit> limits, DateTime? fetchedAt, bool stale, string? problem)
    {
        if (limits.Count == 0) return logs is null || problem is null ? logs : logs with { Label = problem };
        var first = limits[0];
        var basis = logs ?? new AiUsageSnapshot(AiTool.ClaudeCode, null, "", null, 0, Array.Empty<AiSession>());
        return basis with
        {
            Percent = first.UsedPercent, ResetsAt = first.ResetsAt, Label = "", IsLimit = true, Limits = limits, ReportedAt = fetchedAt, IsStale = stale,
            WeekPercent = limits.FirstOrDefault(l => l.WindowMinutes == 10080)?.UsedPercent,
        };
    }

    internal void ShowAiUsage(IReadOnlyList<AiUsageSnapshot> usage)
    {
        _aiUsage = usage.Where(u => u.Percent != null || u.Active.Count > 0).OrderBy(u => u.Tool).ToList();
        if (_aiPage.IsVisible) FillAiPage();
        RefreshToolBar();
        ApplyState(animate: true);
    }

    // ───────────── the rings in the closed notch ─────────────

    /// <summary>
    /// Called by ApplyState for the shape it is about to take: which ends get a ring, the closed views moved in to
    /// make room, and the extra width the notch needs for them.
    /// </summary>
    double PlaceAiWings(NotchState target, bool mini)
    {
        var claude = _aiUsage.FirstOrDefault(u => u.Tool == AiTool.ClaudeCode);
        var codex = _aiUsage.FirstOrDefault(u => u.Tool == AiTool.Codex);
        bool on = _settings.AiUsage && !mini && !_hiddenForFullscreen && (claude != null || codex != null)
                  && target is NotchState.Idle or NotchState.Media or NotchState.Badge or NotchState.Timer or NotchState.Focumon or NotchState.Call or NotchState.Dictation;
        var left = on && claude != null && codex != null ? claude : null;
        var right = on ? codex ?? claude : null;
        if (left != null || right != null) EnsureAiWings();
        if (_aiLeft is { } l) ShowWing(l, left);
        if (_aiRight is { } r) ShowWing(r, right);
        AiWingsShown = (left != null, right != null);

        foreach (var view in new Control[] { _idleView, _compactView, _badgeView, _timerView, _focumonView, _callView, _dictationView })
        {
            if (!_wingBase.TryGetValue(view, out var margin)) _wingBase[view] = margin = view.Margin;
            view.Margin = new Thickness(margin.Left + (left != null ? AiWing : 0), margin.Top, margin.Right + (right != null ? AiWing : 0), margin.Bottom);
        }
        return (left != null ? AiWing : 0) + (right != null ? AiWing : 0);
    }

    void ShowWing((StackPanel Panel, UsageRing Ring, TextBlock Number) wing, AiUsageSnapshot? usage)
    {
        bool on = usage != null;
        if (usage != null)
        {
            wing.Ring.Update(usage, _settings.Animations);
            wing.Number.Text = usage.Percent is { } p ? p.ToString("0", CultureInfo.InvariantCulture) + "%" : "–";
            wing.Number.Foreground = usage.IsStale ? HubTheme.Dim : Look.Ink;
            Avalonia.Automation.AutomationProperties.SetName(wing.Panel, $"{UsageRing.NameOf(usage.Tool)} usage {wing.Number.Text}");
        }
        else wing.Ring.Update(new AiUsageSnapshot(AiTool.ClaudeCode, 0, "", null, 0, Array.Empty<AiSession>()), false); // stops the spin
        wing.Panel.IsHitTestVisible = on;
        double to = on ? 1 : 0;
        if (wing.Panel.Opacity == to && wing.Panel.IsVisible == on) return;
        if (on) wing.Panel.IsVisible = true;
        if (!_settings.Animations)
        {
            StopTween(wing.Panel, "opacity");
            wing.Panel.Opacity = to;
            if (!on) wing.Panel.IsVisible = false;
            return;
        }
        var panel = wing.Panel;
        Animate(panel, "opacity", v => panel.Opacity = v, panel.Opacity, to, on ? 260 : 110, on ? 120 : 0, on ? QuadOut : QuadIn,
                () => { if (!panel.IsHitTestVisible) panel.IsVisible = false; });
    }

    void EnsureAiWings()
    {
        if (_aiWings != null) return;
        (StackPanel, UsageRing, TextBlock) Wing(bool left)
        {
            var ring = new UsageRing(AiRingSize);
            ring.Root.VerticalAlignment = VerticalAlignment.Center;
            var number = new TextBlock { FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = left ? new Thickness(5, 0, 0, 0) : new Thickness(0, 0, 5, 0) };
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = left ? new Thickness(10, 0, 0, 0) : new Thickness(0, 0, 10, 0),
                Opacity = 0, IsVisible = false, IsHitTestVisible = false,
            };
            if (left) { panel.Children.Add(ring.Root); panel.Children.Add(number); }
            else { panel.Children.Add(number); panel.Children.Add(ring.Root); }
            // Hovering a ring opens the notch on the AI page; so does a click.
            panel.PointerEntered += (_, _) => _aiRingHot = true;
            panel.PointerExited += (_, _) => _aiRingHot = false;
            panel.PointerReleased += (_, e) =>
            {
                if (_hovered) return;
                e.Handled = true;
                _expandDelay.Stop();
                _aiRingHot = true;
                SetExpanded(true);
            };
            return (panel, ring, number);
        }
        _aiLeft = Wing(true);
        _aiRight = Wing(false);
        _aiWings = new Grid { Children = { _aiLeft.Value.Panel, _aiRight.Value.Panel } };
        _islandContent.Children.Add(_aiWings);
    }

    /// <summary>Called as the notch opens: from a ring, it opens straight on the AI page.</summary>
    void OpenFromAiRing()
    {
        if (!_aiRingHot || !_settings.AiUsage || _aiUsage.Count == 0) return;
        _aiRingHot = false;
        if (_tool != Tool.Ai) ShowTool(Tool.Ai);
    }

    // ───────────── the AI page in the open notch ─────────────

    void FillAiPage()
    {
        _aiPage.Children.Clear();
        _aiPage.Children.Add(BuildAiPage(_aiUsage, DateTime.UtcNow));
    }

    internal static Control BuildAiPage(IReadOnlyList<AiUsageSnapshot> usage, DateTime nowUtc)
    {
        var page = new StackPanel();
        page.Children.Add(new TextBlock { Text = "AI usage", FontSize = 11, Foreground = HubTheme.Dim, Margin = new Thickness(2, 0, 0, 8) });
        if (usage.Count == 0)
        {
            page.Children.Add(new TextBlock { Text = "Claude Code and Codex show up here once you use them on this " + (OperatingSystem.IsWindows() ? "PC" : "computer"), FontSize = 12, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 4) });
            return page;
        }
        var columns = new UniformGrid { Columns = usage.Count, Rows = 1 };
        for (int i = 0; i < usage.Count; i++)
        {
            var section = AiSection(usage[i], nowUtc);
            section.Margin = new Thickness(i == 0 ? 2 : 10, 0, i == usage.Count - 1 ? 2 : 10, 0);
            columns.Children.Add(section);
        }
        page.Children.Add(columns);
        return page;
    }

    static Control AiSection(AiUsageSnapshot usage, DateTime nowUtc)
    {
        var section = new StackPanel();
        var ring = new UsageRing(34);
        ring.Update(usage, false);
        var number = new TextBlock { FontSize = 22, FontWeight = FontWeight.Light, Foreground = usage.IsStale ? HubTheme.Dim : Look.Ink };
        number.Text = usage.Percent is { } p ? p.ToString("0", CultureInfo.InvariantCulture) + "%" : "–";
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = UsageRing.NameOf(usage.Tool), FontSize = 12, FontWeight = FontWeight.SemiBold });
        words.Children.Add(number);
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(ring.Root);
        head.Children.Add(words);
        section.Children.Add(head);

        if (usage.IsLimit && usage.Limits.Count > 0)
            foreach (var limit in usage.Limits.Take(3)) section.Children.Add(LimitBar(Capital(limit.Name), limit.UsedPercent, limit.ResetsAt, nowUtc));
        else if (usage.Percent is { } estimate)
        {
            section.Children.Add(LimitBar("Estimate · 5 hours", estimate, usage.ResetsAt, nowUtc));
            section.Children.Add(Note(usage.Label is { Length: > 0 } why ? why : "From the logs, compared with your busiest 5 hours"));
        }
        if (usage.IsStale)
            section.Children.Add(Note("Last reported " + (usage.ReportedAt is { } at ? LimitReset(at) : "earlier") + " · may be outdated"));

        foreach (var session in usage.Active.Take(2))
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var dot = new Ellipse { Width = 6, Height = 6, Fill = UsageRing.Ample, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = session.Project + " · " + (session.Step ?? "Working"), FontSize = 11, Foreground = HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
            section.Children.Add(row);
        }
        if (usage.Active.Count > 2) section.Children.Add(Note($"and {usage.Active.Count - 2} more at work"));
        return section;
    }

    static Control LimitBar(string label, double percent, DateTime? resets, DateTime nowUtc)
    {
        percent = Math.Clamp(percent, 0, 100);
        var top = new DockPanel { LastChildFill = true };
        if (resets is { } at)
        {
            var when = new TextBlock { Text = ResetText(at, nowUtc), FontSize = 10.5, Foreground = HubTheme.Faint, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(when, Dock.Right);
            top.Children.Add(when);
        }
        top.Children.Add(new TextBlock { Text = label, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        var fill = new Border { CornerRadius = new CornerRadius(2), Background = UsageRing.ColorOf(percent), HorizontalAlignment = HorizontalAlignment.Left };
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)) });
        bar.Children.Add(fill);
        bar.SizeChanged += (_, e) => fill.Width = percent <= 0 ? 0 : Math.Max(4, e.NewSize.Width * percent / 100);
        var used = new TextBlock { Text = percent.ToString("0", CultureInfo.InvariantCulture) + "% used", FontSize = 10.5, Foreground = HubTheme.Dim, Margin = new Thickness(0, 2, 0, 0) };
        var stack = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        stack.Children.Add(top);
        stack.Children.Add(bar);
        stack.Children.Add(used);
        return stack;
    }

    static TextBlock Note(string text) => new() { Text = text, FontSize = 10.5, Foreground = HubTheme.Faint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>Codenotch's reset wording: "Resets in 51 min", "Resets 18:00", "Resets Thu 09:00".</summary>
    internal static string ResetText(DateTime utc, DateTime nowUtc)
    {
        var left = utc - nowUtc;
        if (left <= TimeSpan.Zero) return "Resetting";
        if (left < TimeSpan.FromHours(1)) return $"Resets in {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min";
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Now.Date
            ? "Resets " + local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : "Resets " + local.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
    }

    internal static string LimitReset(DateTime utc)
        => utc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
}
