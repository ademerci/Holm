using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace WinNotch;

/// <summary>
/// One tool's bubble beside the notch: black like the notch, a ring that fills with the usage and the tool's mark in
/// it. Beside the minimized notch it flattens into a capsule with a bar, so it never stands taller than the notch.
/// </summary>
internal sealed class UsageBubble
{
    public const double Size = 24, MiniWidth = 22, MiniHeight = 6;
    const double RingRadius = 8.4, RingWidth = 2.2, BarWidth = MiniWidth - 4;

    static readonly Brush Coral = HubTheme.Frozen(Color.FromRgb(0xD9, 0x77, 0x57)), Mint = HubTheme.Frozen(Color.FromRgb(0x19, 0xC3, 0x7D));
    static readonly Brush Track = HubTheme.Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    public readonly AiTool Tool;
    public readonly Border Root;
    public AiUsageSnapshot Shown { get; private set; }
    /// <summary>0 beside the minimized notch (a capsule), 1 beside the normal one (a circle).</summary>
    public double Grow { get; private set; } = -1;

    readonly Canvas _ring;
    readonly ScaleTransform _ringScale = new();
    readonly Path _arc;
    readonly Ellipse _working;
    readonly Grid _bar;
    readonly Border _barFill;

    public static Brush ColorOf(AiUsageSnapshot usage) => usage.Percent >= 90 ? HubTheme.Red : usage.Tool == AiTool.ClaudeCode ? Coral : Mint;
    public static string NameOf(AiTool tool) => tool == AiTool.ClaudeCode ? "Claude Code" : "Codex";

    public UsageBubble(AiUsageSnapshot usage)
    {
        Tool = usage.Tool;
        Shown = usage;
        bool claude = Tool == AiTool.ClaudeCode;

        // The circle: a track, the arc on it, the mark in the middle and a dot while an agent works.
        var track = new Ellipse { Width = RingRadius * 2 + RingWidth, Height = RingRadius * 2 + RingWidth, Stroke = Track, StrokeThickness = RingWidth };
        Canvas.SetLeft(track, Size / 2 - RingRadius - RingWidth / 2);
        Canvas.SetTop(track, Size / 2 - RingRadius - RingWidth / 2);
        _arc = new Path { StrokeThickness = RingWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        const double mark = 9.6;
        var logo = new Viewbox
        {
            Width = mark, Height = mark,
            Child = new Canvas { Width = 24, Height = 24, Children = { new Path { Data = AiLauncher.Mark(claude ? "claude" : "chatgpt"), Fill = claude ? Coral : Brushes.White } } },
        };
        Canvas.SetLeft(logo, (Size - mark) / 2);
        Canvas.SetTop(logo, (Size - mark) / 2);
        _working = new Ellipse { Width = 7, Height = 7, Fill = HubTheme.Green, Stroke = Brushes.Black, StrokeThickness = 1.5 };
        Canvas.SetLeft(_working, Size - 7.5);
        Canvas.SetTop(_working, Size - 7.5);
        _ring = new Canvas
        {
            Width = Size, Height = Size, RenderTransform = _ringScale, RenderTransformOrigin = new Point(0.5, 0.5),
            Children = { track, _arc, logo, _working },
        };
        Canvas.SetLeft(_ring, -Size / 2);
        Canvas.SetTop(_ring, -Size / 2);
        // A canvas without a size keeps the ring centred and unclipped while the bubble around it shrinks.
        var centre = new Canvas { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { _ring } };

        // The capsule: the same usage as a bar.
        _barFill = new Border { Height = 2.5, CornerRadius = new CornerRadius(1.25), HorizontalAlignment = HorizontalAlignment.Left };
        _bar = new Grid
        {
            Width = BarWidth, Height = 2.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { CornerRadius = new CornerRadius(1.25), Background = Track }, _barFill },
        };

        Root = new Border
        {
            Background = Brushes.Black, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top, SnapsToDevicePixels = true,
            Child = new Grid { Children = { _bar, centre } },
        };
        Update(usage);
        SetGrow(1);
    }

    public void Update(AiUsageSnapshot usage)
    {
        Shown = usage;
        double percent = Math.Clamp(usage.Percent ?? 0, 0, 100);
        var color = ColorOf(usage);
        _arc.Stroke = color;
        _arc.Data = Arc(percent);
        _barFill.Background = color;
        _barFill.Width = percent <= 0 ? 0 : Math.Max(2.5, BarWidth * percent / 100);
        _working.Visibility = usage.Active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Follows the notch between minimized and normal; cheap enough for every frame of that move.</summary>
    public void SetGrow(double grow)
    {
        grow = Math.Round(Math.Clamp(grow, 0, 1), 2);
        if (grow == Grow) return;
        Grow = grow;
        double height = MiniHeight + (Size - MiniHeight) * grow;
        Root.Width = MiniWidth + (Size - MiniWidth) * grow;
        Root.Height = height;
        Root.CornerRadius = new CornerRadius(height / 2);
        _ringScale.ScaleX = _ringScale.ScaleY = height / Size;
        _ring.Opacity = Math.Clamp((grow - 0.3) / 0.5, 0, 1);
        _bar.Opacity = Math.Clamp(1 - grow / 0.35, 0, 1);
    }

    /// <summary>The arc from twelve o'clock, clockwise, on the ring's 24 × 24 grid.</summary>
    static Geometry? Arc(double percent)
    {
        const double c = Size / 2;
        if (percent < 0.5) return null;
        Geometry arc;
        if (percent >= 99.5) arc = new EllipseGeometry(new Point(c, c), RingRadius, RingRadius);
        else
        {
            double angle = percent / 100 * 2 * Math.PI;
            var end = new Point(c + RingRadius * Math.Sin(angle), c - RingRadius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = new Point(c, c - RingRadius), IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(RingRadius, RingRadius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
            arc = new PathGeometry { Figures = { figure } };
        }
        arc.Freeze();
        return arc;
    }
}

// AI usage beside the notch: a bubble per coding agent you use (Claude Code, Codex), filled by how much of its limit
// is gone, visible in every closed state. Hovering one opens a card with the numbers and what its agents are working
// on right now. Everything comes from the tools' own logs on this PC (AiUsage.cs), read off the UI thread at most once
// a minute; it is shown only here, never sent anywhere and never shared with friends.
public partial class MainWindow
{
    const double AiDockGap = 6, AiBubbleGap = 4;

    StackPanel? _aiDock;
    readonly ScaleTransform _aiDockScale = new();
    readonly TranslateTransform _aiDockAt = new();
    readonly List<UsageBubble> _aiBubbles = new();
    AiUsageReader? _aiClaude, _aiCodex;
    DispatcherTimer? _aiTimer, _aiCardOpen, _aiCardClose;
    Popup? _aiCard;
    UsageBubble? _aiCardFor;
    bool _aiReading, _aiDockOn;
#if DEBUG
    bool _aiDemo; // snapshots show made-up usage and never read the real logs
#else
    const bool _aiDemo = false;
#endif

    internal IReadOnlyList<UsageBubble> AiBubbles => _aiBubbles;
    internal bool AiDockShown => _aiDockOn;

    void StartAiUsage()
    {
        _aiTimer?.Stop();
        if (!_settings.AiUsage)
        {
            ShowAiUsage(Array.Empty<AiUsageSnapshot>());
            return;
        }
        _aiTimer ??= MakeTimer(60_000, () => _ = RefreshAiUsageAsync());
        _aiTimer.Interval = TimeSpan.FromSeconds(8); // the first look waits until start-up is over
        _aiTimer.Start();
    }

    void StopAiUsage()
    {
        _aiTimer?.Stop();
        CloseAiCard();
    }

    async Task RefreshAiUsageAsync()
    {
        if (_aiReading || _aiDemo || !_settings.AiUsage || _aiTimer == null) return;
        _aiTimer.Interval = TimeSpan.FromMinutes(1);
        if (_hiddenForFullscreen) return; // nothing shows over a game: look again when it's over
        _aiReading = true;
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var claude = _aiClaude ??= new AiUsageReader(AiTool.ClaudeCode, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c : System.IO.Path.Combine(home, ".claude"));
            var codex = _aiCodex ??= new AiUsageReader(AiTool.Codex, Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } x ? x : System.IO.Path.Combine(home, ".codex"));
            var now = DateTime.UtcNow;
            var found = await Task.Run(() => new[] { claude.Read(now), codex.Read(now) });
            if (_aiDemo || !_settings.AiUsage) return;
            var usage = found.Where(u => u != null).Select(u => u!).ToList();
            ShowAiUsage(usage);
            // Numbers only: what the agents work on stays out of the log.
            foreach (var u in usage) Diag.Log(string.Create(CultureInfo.InvariantCulture, $"AI usage: {u.Tool} {u.Percent:0} %, {u.TokensInWindow} tokens, {u.Active.Count} working"));
            // Neither tool on this PC: look again now and then, in case one gets installed.
            if (usage.Count == 0) _aiTimer.Interval = TimeSpan.FromMinutes(15);
        }
        catch (Exception ex) { Diag.Log("AI usage: " + ex.GetType().Name + ": " + ex.Message); }
        finally { _aiReading = false; }
    }

    /// <summary>Shows a bubble for each tool with usage, in a fixed order, and takes away the others.</summary>
    internal void ShowAiUsage(IReadOnlyList<AiUsageSnapshot> usage)
    {
        if (usage.Count == 0 && _aiDock == null) return;
        EnsureAiDock();
        foreach (var gone in _aiBubbles.Where(b => usage.All(u => u.Tool != b.Tool)).ToList())
        {
            _aiBubbles.Remove(gone);
            _aiDock!.Children.Remove(gone.Root);
            if (_aiCardFor == gone) CloseAiCard();
        }
        foreach (var tool in usage.OrderBy(u => u.Tool))
        {
            if (_aiBubbles.FirstOrDefault(b => b.Tool == tool.Tool) is { } known) { known.Update(tool); continue; }
            var bubble = new UsageBubble(tool);
            bubble.Root.MouseEnter += (_, _) => HoverAiBubble(bubble, true);
            bubble.Root.MouseLeave += (_, _) => HoverAiBubble(bubble, false);
            int at = _aiBubbles.Count(b => b.Tool < tool.Tool);
            _aiBubbles.Insert(at, bubble);
            _aiDock!.Children.Insert(at, bubble.Root);
        }
        for (int i = 0; i < _aiBubbles.Count; i++) _aiBubbles[i].Root.Margin = new Thickness(i == 0 ? 0 : AiBubbleGap, 0, 0, 0);
        if (_aiCard is { IsOpen: true } && _aiCardFor != null) FillAiCard(); // the open card follows
        ShowAiDock();
    }

    void EnsureAiDock()
    {
        if (_aiDock != null) return;
        _aiDock = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new TransformGroup { Children = { _aiDockScale, _aiDockAt } },
            Opacity = 0, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        // Behind the notch: opening, the notch grows over the bubbles, and closing uncovers them again.
        Root.Children.Insert(0, _aiDock);
        _aiCardOpen = MakeTimer(140, OpenAiCard, oneShot: true);
        _aiCardClose = MakeTimer(240, CloseAiCard, oneShot: true);
    }

    /// <summary>
    /// Whether the bubbles show: beside every closed notch that leaves them room, not beside the open one or a
    /// pop-out that asks something (those take the width). Called when the state or the usage changed.
    /// </summary>
    void ShowAiDock()
    {
        if (_aiDock == null) return;
        double width = _aiBubbles.Count * UsageBubble.Size + Math.Max(0, _aiBubbles.Count - 1) * AiBubbleGap;
        bool room = (Math.Max(_shapeW, 0) / 2 + MaxEar + AiDockGap + width) * _shapeScale <= AiWindowWidth / 2;
        bool on = _aiBubbles.Count > 0 && _settings.AiUsage && !_hiddenForFullscreen && room
                  && _state is not (NotchState.None or NotchState.Expanded or NotchState.Share or NotchState.Task or NotchState.Phone);
        if (on) PlaceAiDock(_wSpring.Value, _hSpring.Value, Ear);
        if (on == _aiDockOn) return;
        _aiDockOn = on;
        _aiDock.IsHitTestVisible = on;
        if (on)
        {
            _aiDock.Visibility = Visibility.Visible;
            PlaceAiDock(_wSpring.Value, _hSpring.Value, Ear);
        }
        else CloseAiCard();

        if (!_settings.Animations)
        {
            _aiDock.BeginAnimation(OpacityProperty, null);
            _aiDock.Opacity = on ? 1 : 0;
            if (!on) _aiDock.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 240 : 90))
        {
            BeginTime = TimeSpan.FromMilliseconds(on ? 140 : 0), // the closing notch uncovers them first
            EasingFunction = on ? FadeInEase : FadeOut,
        };
        if (!on) fade.Completed += (_, _) => { if (!_aiDockOn && _aiDock != null) _aiDock.Visibility = Visibility.Collapsed; };
        _aiDock.BeginAnimation(OpacityProperty, fade);
    }

    double AiWindowWidth => Root.ActualWidth > 0 ? Root.ActualWidth : Width;

    /// <summary>
    /// Keeps the bubbles at the notch's right shoulder. Runs on every frame the shape moves, so it only sets two
    /// transforms, and resizes the bubbles only on the way between the minimized and the normal notch.
    /// </summary>
    void PlaceAiDock(double w, double h, double ear)
    {
        if (!_aiDockOn && _aiDock?.Visibility != Visibility.Visible) return;
        double scale = _notchScale.ScaleX;
        double grow = Math.Clamp((h - 10) / 14, 0, 1);
        for (int i = 0; i < _aiBubbles.Count; i++) _aiBubbles[i].SetGrow(grow);
        _aiDockScale.ScaleX = _aiDockScale.ScaleY = scale;
        _aiDockAt.X = AiWindowWidth / 2 + (w / 2 + ear + AiDockGap) * scale;
        _aiDockAt.Y = 3 * grow * scale;
    }

    // ───────────── the card ─────────────

    void HoverAiBubble(UsageBubble bubble, bool over)
    {
        if (_aiCardOpen == null || _aiCardClose == null) return;
        if (!over)
        {
            _aiCardOpen.Stop();
            _aiCardClose.Start();
            return;
        }
        _aiCardClose.Stop();
        bool other = _aiCardFor != bubble;
        _aiCardFor = bubble;
        if (_aiCard is { IsOpen: true })
        {
            if (other) OpenAiCard(); // straight over to the neighbour
        }
        else _aiCardOpen.Start();
    }

    void OpenAiCard()
    {
        if (_aiCardFor == null || !_aiDockOn || _hiddenForFullscreen) return;
        _aiCard ??= new Popup
        {
            AllowsTransparency = true, StaysOpen = true, Focusable = false, PopupAnimation = PopupAnimation.None,
            Placement = PlacementMode.Custom,
            // Centred under the bubble; Windows keeps it on the screen.
            CustomPopupPlacementCallback = (popup, target, _) => new[] { new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, target.Height + 2), PopupPrimaryAxis.Horizontal) },
        };
        _aiCard.IsOpen = false; // a new target only takes effect on opening
        _aiCard.PlacementTarget = _aiCardFor.Root;
        FillAiCard();
        _aiCard.IsOpen = true;
        if (!_settings.Animations || _aiCard.Child is not FrameworkElement card) return;
        var rise = new TranslateTransform();
        card.RenderTransform = rise;
        rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = Spring.Smooth });
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = FadeInEase });
    }

    void FillAiCard()
    {
        if (_aiCard == null || _aiCardFor == null || _aiCardClose == null) return;
        var card = BuildAiCard(_aiCardFor.Shown);
        var close = _aiCardClose;
        card.MouseEnter += (_, _) => close.Stop();
        card.MouseLeave += (_, _) => close.Start();
        _aiCard.Child = card;
    }

    void CloseAiCard()
    {
        _aiCardOpen?.Stop();
        _aiCardClose?.Stop();
        if (_aiCard != null) _aiCard.IsOpen = false;
        _aiCardFor = null;
    }

    static readonly FontFamily AiCardDisplay = new("Segoe UI Variable Display, Segoe UI");
    static readonly Brush AiCardBack = HubTheme.Frozen(Color.FromRgb(0x0E, 0x0E, 0x10)), AiCardRow = HubTheme.Frozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));

    /// <summary>The card under a bubble: how much is used, when it starts over, and the agents at work.</summary>
    internal static FrameworkElement BuildAiCard(AiUsageSnapshot usage)
    {
        const double width = 320, pad = 16, inner = width - 2 * pad;
        bool claude = usage.Tool == AiTool.ClaudeCode;
        double percent = Math.Clamp(usage.Percent ?? 0, 0, 100);
        var color = UsageBubble.ColorOf(usage);
        var rows = new StackPanel();

        // Mark, name and what the number means on the left; the number on the right.
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (AiLauncher.Tile(claude ? "claude" : "chatgpt", 34) is { } tile) head.Children.Add(tile);
        string meaning = claude
            ? usage.TokensInWindow > 0 && usage.ResetsAt is { } ends ? $"5-hour window · ends {Clock(ends)}" : "Nothing in the last 5 hours"
            : usage.ResetsAt is { } resets ? $"5-hour limit · resets {Clock(resets)}" : "5-hour limit";
        var names = new StackPanel
        {
            Margin = new Thickness(11, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { Line(UsageBubble.NameOf(usage.Tool), 14.5, Brushes.White, FontWeights.SemiBold), Line(meaning, 11.5, HubTheme.Dim) },
        };
        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        var number = new TextBlock
        {
            Text = usage.Percent == null ? "–" : percent.ToString("0", CultureInfo.InvariantCulture) + " %",
            FontFamily = AiCardDisplay, FontSize = 25, FontWeight = FontWeights.Light, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(number, 2);
        head.Children.Add(number);
        rows.Children.Add(head);

        rows.Children.Add(Meter(percent, color, inner, 6, new Thickness(0, 14, 0, 0)));

        // Under the bar: Claude Code logs tokens, so say what the ring compares; Codex logs its week.
        if (claude)
            rows.Children.Add(Pair($"{Count(usage.TokensInWindow)} tokens", usage.BusiestTokens > 0 ? $"your busiest 5 hours: {Count(usage.BusiestTokens)}" : "", 8));
        else if (usage.WeekPercent is { } week)
        {
            rows.Children.Add(Pair("This week", week.ToString("0", CultureInfo.InvariantCulture) + " %", 10));
            rows.Children.Add(Meter(Math.Clamp(week, 0, 100), week >= 90 ? HubTheme.Red : HubTheme.Dim, inner, 3, new Thickness(0, 6, 0, 0)));
        }

        rows.Children.Add(new Border { Height = 1, Background = HubTheme.Line, Margin = new Thickness(0, 14, 0, 12) });
        rows.Children.Add(new TextBlock { Text = "WORKING NOW", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = HubTheme.Faint, Margin = new Thickness(1, 0, 0, 7) });
        if (usage.Active.Count == 0) rows.Children.Add(Line("No agent is working right now.", 12, HubTheme.Dim));
        foreach (var session in usage.Active.Take(4))
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = HubTheme.Green, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
            var text = new StackPanel { Children = { Line(session.Project, 12.5, Brushes.White, FontWeights.SemiBold), Line(session.Step ?? "Working", 11.5, HubTheme.Dim) } };
            var row = new DockPanel { Children = { dot, text } };
            rows.Children.Add(new Border { Background = AiCardRow, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 0, 0, 5), Child = row });
        }
        if (usage.Active.Count > 4) rows.Children.Add(Line($"and {usage.Active.Count - 4} more", 11.5, HubTheme.Faint));

        var card = new Border
        {
            Width = width, Background = AiCardBack, BorderBrush = HubTheme.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22),
            Padding = new Thickness(pad, pad, pad, pad - 3), Child = rows,
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 7, Direction = 270, Opacity = 0.5, Color = Colors.Black },
        };
        TextElement.SetFontFamily(card, HubTheme.Text);
        // Room for the shadow; the popup sits this much closer to the bubble.
        return new Grid { Width = width + 40, Children = { new Border { Margin = new Thickness(20, 6, 20, 30), Child = card } } };
    }

    static TextBlock Line(string text, double size, Brush brush, FontWeight? weight = null)
        => new() { Text = text, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };

    static Grid Pair(string left, string right, double top)
    {
        var dim = Line(right, 11.5, HubTheme.Dim);
        dim.HorizontalAlignment = HorizontalAlignment.Right;
        return new Grid { Margin = new Thickness(0, top, 0, 0), Children = { Line(left, 11.5, HubTheme.Dim), dim } };
    }

    static Grid Meter(double percent, Brush color, double width, double height, Thickness margin) => new()
    {
        Height = height, Margin = margin,
        Children =
        {
            new Border { CornerRadius = new CornerRadius(height / 2), Background = HubTheme.CardStrong },
            new Border { CornerRadius = new CornerRadius(height / 2), Background = color, HorizontalAlignment = HorizontalAlignment.Left, Width = percent <= 0 ? 0 : Math.Max(height, width * percent / 100) },
        },
    };

    static string Clock(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    static string Count(long tokens) => tokens >= 1_000_000 ? (tokens / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M"
                                      : tokens >= 1000 ? (tokens / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "K"
                                      : tokens.ToString(CultureInfo.InvariantCulture);

#if DEBUG
    /// <summary>Snapshot poses with made-up usage: the bubbles idle, with music and minimized, and both cards.</summary>
    async Task SnapAiUsageAsync(string dir)
    {
        _aiDemo = true; // from here on the real logs are never read
        _aiTimer?.Stop();
        var now = DateTime.UtcNow;
        var claude = new AiUsageSnapshot(AiTool.ClaudeCode, 62, "62 % of your busiest 5 hours", now.AddHours(2.4), 1_240_000, new[]
        {
            new AiSession(AiTool.ClaudeCode, "holm", "Bash: Run the tests", now),
            new AiSession(AiTool.ClaudeCode, "website", "Edit: home.css", now),
        }) { BusiestTokens = 2_000_000 };
        var codex = new AiUsageSnapshot(AiTool.Codex, 28, "28 % of the 5-hour limit", now.AddHours(3.2), 310_000, Array.Empty<AiSession>()) { IsLimit = true, WeekPercent = 12 };

        OnMediaChanged(null, false);
        ShowAiUsage(new[] { claude, codex });
        await Snap(dir, "ai-usage", 60);
        OnMediaChanged(DemoMedia(), true);
        await Snap(dir, "ai-usage-music", 60);
        OnMediaChanged(null, false);
        _settings.MiniIdle = true;
        ApplyState(animate: false);
        await Snap(dir, "ai-usage-mini", 40);
        _settings.MiniIdle = false;
        SnapElement(BuildAiCard(claude), dir, "ai-usage-card");
        SnapElement(BuildAiCard(codex), dir, "ai-usage-card-codex");
        ShowAiUsage(Array.Empty<AiUsageSnapshot>());
        ApplyState(animate: false);
    }

    static void SnapElement(FrameworkElement element, string dir, string name)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        double width = Math.Ceiling(element.DesiredSize.Width), height = Math.Ceiling(element.DesiredSize.Height);
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
        var desktop = new DrawingVisual();
        using (var dc = desktop.RenderOpen())
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5B, 0x7F, 0xC9), Color.FromRgb(0x1D, 0x2B, 0x4F), 90), null, new Rect(0, 0, width, height));
        image.Render(desktop);
        image.Render(element);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var file = System.IO.File.Create(System.IO.Path.Combine(dir, name + ".png"));
        png.Save(file);
    }
#endif

    void MenuAiUsage_Click(object sender, RoutedEventArgs e)
    {
        _settings.AiUsage = MenuAiUsage.IsChecked;
        _settings.Save();
        StartAiUsage();
        if (_settings.AiUsage) _ = RefreshAiUsageAsync(); // switched on: show it now
    }
}
