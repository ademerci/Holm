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
/// The bubble beside the notch, like a second activity on the iPhone's island: a black circle with the agent's mark and
/// a thin ring that fills with its usage. Beside the minimized notch it flattens into a capsule with a bar, so it never
/// stands taller than the notch.
/// </summary>
internal sealed class UsageBubble
{
    public const double Size = 26, MiniWidth = 22, MiniHeight = 6;
    const double RingRadius = 11.2, RingWidth = 1.8, BarWidth = MiniWidth - 4, MarkSize = 12.5;

    static readonly Brush Track = HubTheme.Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    static readonly Brush Coral = HubTheme.Frozen(AiMarks.ClaudeColor);

    public readonly Border Root;
    public AiUsageSnapshot Shown { get; private set; }
    public double Grow { get; private set; } = -1;

    readonly Canvas _ring;
    readonly ScaleTransform _ringScale = new();
    readonly Path _arc;
    readonly ContentControl _mark = new() { Focusable = false, IsTabStop = false };
    readonly Grid _bar;
    readonly Border _barFill;
    AiTool? _markFor;

    /// <summary>The tool's own colour, red once nearly used up.</summary>
    public static Brush ColorOf(AiUsageSnapshot usage) => usage.Percent >= 90 ? HubTheme.Red : usage.Tool == AiTool.ClaudeCode ? Coral : Brushes.White;
    public static string NameOf(AiTool tool) => tool == AiTool.ClaudeCode ? "Claude Code" : "Codex";

    public UsageBubble(AiUsageSnapshot usage)
    {
        Shown = usage;
        const double c = Size / 2;
        var track = new Ellipse { Width = RingRadius * 2, Height = RingRadius * 2, Stroke = Track, StrokeThickness = RingWidth };
        Canvas.SetLeft(track, c - RingRadius);
        Canvas.SetTop(track, c - RingRadius);
        _arc = new Path { StrokeThickness = RingWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        Canvas.SetLeft(_mark, c - MarkSize / 2);
        Canvas.SetTop(_mark, c - MarkSize / 2);
        _ring = new Canvas
        {
            Width = Size, Height = Size, RenderTransform = _ringScale, RenderTransformOrigin = new Point(0.5, 0.5),
            Children = { track, _arc, _mark },
        };
        Canvas.SetLeft(_ring, -Size / 2);
        Canvas.SetTop(_ring, -Size / 2);
        // A canvas without a size keeps the ring centred and unclipped while the bubble around it shrinks.
        var centre = new Canvas { Width = 0, Height = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { _ring } };

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
        if (_markFor != usage.Tool)
        {
            _markFor = usage.Tool;
            _mark.Content = AiMarks.For(usage.Tool, MarkSize);
        }
        double percent = Math.Clamp(usage.Percent ?? 0, 0, 100);
        var color = ColorOf(usage);
        _arc.Stroke = color;
        _arc.Data = Arc(percent);
        _barFill.Background = color;
        _barFill.Width = percent <= 0 ? 0 : Math.Max(2.5, BarWidth * percent / 100);
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

    /// <summary>The arc from twelve o'clock, clockwise.</summary>
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

// AI usage beside the notch: one bubble for the coding agent that matters most right now (the one at work, else the one
// further along), visible in every closed state. Hovering it opens a card with every agent you use: how much is gone,
// when it starts over, and which sessions are working on what. Everything comes from the tools' own logs on this PC
// (AiUsage.cs), read off the UI thread at most once a minute; it is shown only here, never sent anywhere or shared.
public partial class MainWindow
{
    const double AiDockGap = 6;

    Border? _aiDock;
    readonly ScaleTransform _aiDockScale = new();
    readonly TranslateTransform _aiDockAt = new();
    UsageBubble? _aiBubble;
    IReadOnlyList<AiUsageSnapshot> _aiUsage = Array.Empty<AiUsageSnapshot>();
    AiUsageReader? _aiClaude, _aiCodex;
    DispatcherTimer? _aiTimer, _aiCardOpen, _aiCardClose;
    Popup? _aiCard;
    bool _aiReading, _aiDockOn;
#if DEBUG
    bool _aiDemo; // snapshots show made-up usage and never read the real logs
#else
    const bool _aiDemo = false;
#endif

    internal UsageBubble? AiBubble => _aiBubble;
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

    /// <summary>The agent the bubble shows: one at work, else the one further along, Claude Code on a tie.</summary>
    internal static AiUsageSnapshot? Leading(IReadOnlyList<AiUsageSnapshot> usage)
        => usage.OrderByDescending(u => u.Active.Count > 0).ThenByDescending(u => u.Percent ?? 0).ThenBy(u => u.Tool).FirstOrDefault();

    internal void ShowAiUsage(IReadOnlyList<AiUsageSnapshot> usage)
    {
        _aiUsage = usage.OrderBy(u => u.Tool).ToList();
        var lead = Leading(_aiUsage);
        if (lead == null && _aiDock == null) return;
        EnsureAiDock();
        if (lead != null)
        {
            if (_aiBubble == null)
            {
                _aiBubble = new UsageBubble(lead);
                _aiDock!.Child = _aiBubble.Root;
            }
            else _aiBubble.Update(lead);
        }
        if (_aiCard is { IsOpen: true })
        {
            if (lead == null) CloseAiCard();
            else _aiCard.Child = AiCard(); // the open card follows
        }
        ShowAiDock();
    }

    void EnsureAiDock()
    {
        if (_aiDock != null) return;
        _aiDock = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent,
            RenderTransform = new TransformGroup { Children = { _aiDockScale, _aiDockAt } },
            Opacity = 0, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        _aiDock.MouseEnter += (_, _) => HoverAiBubble(true);
        _aiDock.MouseLeave += (_, _) => HoverAiBubble(false);
        // Behind the notch: opening, the notch grows over the bubble, and closing uncovers it again.
        Root.Children.Insert(0, _aiDock);
        _aiCardOpen = MakeTimer(140, OpenAiCard, oneShot: true);
        _aiCardClose = MakeTimer(240, CloseAiCard, oneShot: true);
    }

    /// <summary>
    /// Whether the bubble shows: beside every closed notch that leaves it room, not beside the open one or a pop-out
    /// that asks something (those take the width).
    /// </summary>
    void ShowAiDock()
    {
        if (_aiDock == null) return;
        bool room = (Math.Max(_shapeW, 0) / 2 + MaxEar + AiDockGap + UsageBubble.Size) * _shapeScale <= AiWindowWidth / 2;
        bool on = _aiUsage.Count > 0 && _aiBubble != null && _settings.AiUsage && !_hiddenForFullscreen && room
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
            BeginTime = TimeSpan.FromMilliseconds(on ? 140 : 0), // the closing notch uncovers it first
            EasingFunction = on ? FadeInEase : FadeOut,
        };
        if (!on) fade.Completed += (_, _) => { if (!_aiDockOn && _aiDock != null) _aiDock.Visibility = Visibility.Collapsed; };
        _aiDock.BeginAnimation(OpacityProperty, fade);
    }

    double AiWindowWidth => Root.ActualWidth > 0 ? Root.ActualWidth : Width;

    /// <summary>
    /// Keeps the bubble at the notch's right shoulder. Runs on every frame the shape moves, so it only sets two
    /// transforms, and resizes the bubble only on the way between the minimized and the normal notch.
    /// </summary>
    void PlaceAiDock(double w, double h, double ear)
    {
        if (!_aiDockOn && _aiDock?.Visibility != Visibility.Visible) return;
        double scale = _notchScale.ScaleX;
        double grow = Math.Clamp((h - 10) / 14, 0, 1);
        _aiBubble?.SetGrow(grow);
        _aiDockScale.ScaleX = _aiDockScale.ScaleY = scale;
        _aiDockAt.X = AiWindowWidth / 2 + (w / 2 + ear + AiDockGap) * scale;
        _aiDockAt.Y = 2 * grow * scale;
    }

    // ───────────── the card ─────────────

    void HoverAiBubble(bool over)
    {
        if (_aiCardOpen == null || _aiCardClose == null) return;
        if (!over)
        {
            _aiCardOpen.Stop();
            _aiCardClose.Start();
            return;
        }
        _aiCardClose.Stop();
        if (_aiCard is not { IsOpen: true }) _aiCardOpen.Start();
    }

    void OpenAiCard()
    {
        if (_aiDock == null || !_aiDockOn || _hiddenForFullscreen || _aiUsage.Count == 0) return;
        _aiCard ??= new Popup
        {
            AllowsTransparency = true, StaysOpen = true, Focusable = false, PopupAnimation = PopupAnimation.None,
            Placement = PlacementMode.Custom, PlacementTarget = _aiDock,
            // Centred under the bubble; Windows keeps it on the screen.
            CustomPopupPlacementCallback = (popup, target, _) => new[] { new CustomPopupPlacement(new Point((target.Width - popup.Width) / 2, target.Height - 4), PopupPrimaryAxis.Horizontal) },
        };
        _aiCard.Child = AiCard();
        _aiCard.IsOpen = true;
        if (!_settings.Animations || _aiCard.Child is not FrameworkElement card) return;
        var rise = new TranslateTransform();
        card.RenderTransform = rise;
        rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = Spring.Smooth });
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = FadeInEase });
    }

    FrameworkElement AiCard()
    {
        var card = BuildAiCard(_aiUsage);
        card.MouseEnter += (_, _) => _aiCardClose?.Stop();
        card.MouseLeave += (_, _) => _aiCardClose?.Start();
        return card;
    }

    void CloseAiCard()
    {
        _aiCardOpen?.Stop();
        _aiCardClose?.Stop();
        if (_aiCard != null) _aiCard.IsOpen = false;
    }

    static readonly FontFamily AiCardDisplay = new("Segoe UI Variable Display, Segoe UI");
    static readonly Brush AiCardEdge = HubTheme.Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    static readonly Brush AiTrack = HubTheme.Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    const double AiCardWidth = 288, AiCardPad = 18;

    /// <summary>The card under the bubble: one calm section per agent, the black of the notch around them.</summary>
    internal static FrameworkElement BuildAiCard(IReadOnlyList<AiUsageSnapshot> usage)
    {
        var sections = new StackPanel();
        for (int i = 0; i < usage.Count; i++)
        {
            if (i > 0) sections.Children.Add(new Border { Height = 1, Background = HubTheme.Line, Margin = new Thickness(0, 16, 0, 16) });
            sections.Children.Add(AiSection(usage[i]));
        }
        var card = new Border
        {
            Width = AiCardWidth, Background = Brushes.Black, BorderBrush = AiCardEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24),
            Padding = new Thickness(AiCardPad, AiCardPad - 2, AiCardPad, AiCardPad), Child = sections,
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.45, Color = Colors.Black },
        };
        TextElement.SetFontFamily(card, HubTheme.Text);
        TextElement.SetForeground(card, Brushes.White);
        // Room for the shadow.
        return new Grid { Width = AiCardWidth + 40, Children = { new Border { Margin = new Thickness(20, 8, 20, 32), Child = card } } };
    }

    static FrameworkElement AiSection(AiUsageSnapshot usage)
    {
        const double inner = AiCardWidth - 2 * AiCardPad - 2;
        double percent = Math.Clamp(usage.Percent ?? 0, 0, 100);
        var section = new StackPanel();

        // The mark and the name; when it starts over on the right.
        var head = new DockPanel { LastChildFill = true };
        var mark = AiMarks.For(usage.Tool, 15);
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(mark, Dock.Left);
        head.Children.Add(mark);
        if (usage.ResetsAt is { } resets && (usage.IsLimit || usage.TokensInWindow > 0))
        {
            var when = Small("Resets " + Clock(resets), HubTheme.Faint);
            when.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(when, Dock.Right);
            head.Children.Add(when);
        }
        head.Children.Add(new TextBlock { Text = UsageBubble.NameOf(usage.Tool), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        section.Children.Add(head);

        // The number, big and light, and what it means in one line.
        var number = new TextBlock { FontFamily = AiCardDisplay, Margin = new Thickness(-1, 10, 0, 0), Foreground = Brushes.White };
        number.Inlines.Add(new Run(usage.Percent == null ? "–" : percent.ToString("0", CultureInfo.InvariantCulture)) { FontSize = 34, FontWeight = FontWeights.Light });
        if (usage.Percent != null) number.Inlines.Add(new Run(" %") { FontSize = 17, FontWeight = FontWeights.Light, Foreground = HubTheme.Dim });
        section.Children.Add(number);
        string meaning = usage.IsLimit
            ? "of the 5-hour limit" + (usage.WeekPercent is { } week ? string.Create(CultureInfo.InvariantCulture, $" · {week:0} % this week") : "")
            : usage.TokensInWindow > 0 ? "of your busiest 5 hours this week" : "Nothing in the last 5 hours";
        section.Children.Add(Small(meaning, HubTheme.Dim));

        var fill = new Border
        {
            CornerRadius = new CornerRadius(2), Background = UsageBubble.ColorOf(usage), HorizontalAlignment = HorizontalAlignment.Left,
            Width = percent <= 0 ? 0 : Math.Max(4, inner * percent / 100),
        };
        section.Children.Add(new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 0), Children = { new Border { CornerRadius = new CornerRadius(2), Background = AiTrack }, fill } });

        // The sessions at work, one quiet line each.
        foreach (var session in usage.Active.Take(3))
        {
            var row = new DockPanel { Margin = new Thickness(0, section.Children.Count == 4 ? 14 : 7, 0, 0) };
            var dot = new Ellipse { Width = 6, Height = 6, Fill = HubTheme.Green, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 9, 0) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            var project = new TextBlock { Text = session.Project, FontSize = 12.5, FontWeight = FontWeights.SemiBold, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(project, Dock.Left);
            row.Children.Add(project);
            var step = Small(session.Step ?? "Working", HubTheme.Dim);
            step.Margin = new Thickness(10, 0, 0, 0);
            step.TextAlignment = TextAlignment.Right;
            step.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(step);
            section.Children.Add(row);
        }
        if (usage.Active.Count > 3) section.Children.Add(Small($"and {usage.Active.Count - 3} more at work", HubTheme.Faint));
        return section;
    }

    static TextBlock Small(string text, Brush brush) => new() { Text = text, FontSize = 12, Foreground = brush, TextTrimming = TextTrimming.CharacterEllipsis };

    static string Clock(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

#if DEBUG
    /// <summary>Snapshot poses with made-up usage: the bubble idle, with music and minimized, and the card.</summary>
    async Task SnapAiUsageAsync(string dir)
    {
        _aiDemo = true; // from here on the real logs are never read
        _aiTimer?.Stop();
        var now = DateTime.UtcNow;
        var claude = new AiUsageSnapshot(AiTool.ClaudeCode, 62, "", now.AddHours(2.4), 1_240_000, new[]
        {
            new AiSession(AiTool.ClaudeCode, "holm", "Bash: Run the tests", now),
            new AiSession(AiTool.ClaudeCode, "website", "Edit: home.css", now),
        }) { BusiestTokens = 2_000_000 };
        var codex = new AiUsageSnapshot(AiTool.Codex, 28, "", now.AddHours(3.2), 310_000, Array.Empty<AiSession>()) { IsLimit = true, WeekPercent = 12 };

        _peekActive = false; // nothing else may stand beside the notch in these pictures
        _peekTimer.Stop();
        if (TaskShowing) HideTask();
        OnMediaChanged(null, false);
        ShowAiUsage(new[] { claude, codex });
        await Snap(dir, "ai-usage", 60);
        OnMediaChanged(DemoMedia(), true);
        await Snap(dir, "ai-usage-music", 60);
        OnMediaChanged(null, false);
        ShowAiUsage(new[] { codex });
        await Snap(dir, "ai-usage-codex", 60);
        ShowAiUsage(new[] { claude, codex });
        _settings.MiniIdle = true;
        ApplyState(animate: false);
        await Snap(dir, "ai-usage-mini", 40);
        _settings.MiniIdle = false;
        SnapElement(BuildAiCard(new[] { claude, codex }), dir, "ai-usage-card");
        SnapElement(BuildAiCard(new[] { codex }), dir, "ai-usage-card-codex");
        SnapElement(new StackPanel
        {
            Orientation = Orientation.Horizontal, Background = Brushes.Black,
            Children = { AiMarks.For(AiTool.ClaudeCode, 96), new Border { Width = 24 }, AiMarks.For(AiTool.Codex, 96) },
        }, dir, "ai-marks");
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
