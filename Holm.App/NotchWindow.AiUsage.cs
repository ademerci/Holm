using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The bubble beside the notch, like a second activity on the iPhone's island: a black circle with the agent's mark and
/// a thin ring that fills with its usage. Beside the minimized notch it flattens into a capsule with a bar, so it never
/// stands taller than the notch.
/// </summary>
internal sealed class UsageBubble
{
    public const double Size = 26, MiniWidth = 22, MiniHeight = 6;
    const double RingRadius = 11.2, RingWidth = 1.8, BarWidth = MiniWidth - 4, MarkSize = 12.5;

    static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)).ToImmutable();
    static readonly IBrush Coral = new SolidColorBrush(AiMarks.ClaudeColor).ToImmutable();

    public readonly Border Root;
    public AiUsageSnapshot Shown { get; private set; }
    public double Grow { get; private set; } = -1;

    readonly Canvas _ring;
    readonly ScaleTransform _ringScale = new(1, 1);
    readonly Path _arc;
    readonly Border _mark = new();
    readonly Grid _bar;
    readonly Border _barFill;
    AiTool? _markFor;

    /// <summary>The tool's own colour, red once nearly used up.</summary>
    public static IBrush ColorOf(AiUsageSnapshot usage) => usage.Percent >= 90 ? HubTheme.Red : usage.Tool == AiTool.ClaudeCode ? Coral : Brushes.White;
    public static string NameOf(AiTool tool) => tool == AiTool.ClaudeCode ? "Claude Code" : "Codex";

    public UsageBubble(AiUsageSnapshot usage)
    {
        Shown = usage;
        const double c = Size / 2;
        var track = new Ellipse { Width = RingRadius * 2, Height = RingRadius * 2, Stroke = Track, StrokeThickness = RingWidth };
        Canvas.SetLeft(track, c - RingRadius);
        Canvas.SetTop(track, c - RingRadius);
        _arc = new Path { StrokeThickness = RingWidth, StrokeLineCap = PenLineCap.Round };
        Canvas.SetLeft(_mark, c - MarkSize / 2);
        Canvas.SetTop(_mark, c - MarkSize / 2);
        _ring = new Canvas
        {
            Width = Size, Height = Size, RenderTransform = _ringScale, RenderTransformOrigin = RelativePoint.Center,
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
            Background = Brushes.Black, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top,
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
            _mark.Child = AiMarks.For(usage.Tool, MarkSize);
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
        if (percent >= 99.5) return new EllipseGeometry { Center = new Point(c, c), RadiusX = RingRadius, RadiusY = RingRadius };
        double angle = percent / 100 * 2 * Math.PI;
        var end = new Point(c + RingRadius * Math.Sin(angle), c - RingRadius * Math.Cos(angle));
        var arc = new StreamGeometry();
        using var ctx = arc.Open();
        ctx.BeginFigure(new Point(c, c - RingRadius), false);
        ctx.ArcTo(end, new Size(RingRadius, RingRadius), 0, angle > Math.PI, SweepDirection.Clockwise);
        ctx.EndFigure(false);
        return arc;
    }
}

// AI usage beside the notch: one bubble for the coding agent that matters most right now (the one at work, else the one
// further along), visible in every closed state. Hovering it opens a card with every agent you use: how much is gone,
// when it starts over, and which sessions are working on what. Everything comes from the tools' own logs on this PC
// (AiUsage.cs), read off the UI thread at most once a minute; it is shown only here, never sent anywhere or shared.
public sealed partial class NotchWindow
{
    const double AiDockGap = 6;

    Border? _aiDock;
    UsageBubble? _aiBubble;
    IReadOnlyList<AiUsageSnapshot> _aiUsage = Array.Empty<AiUsageSnapshot>();
    AiUsageReader? _aiClaude, _aiCodex;
    DispatcherTimer? _aiTimer, _aiCardOpen, _aiCardClose;
    Popup? _aiCard;
    bool _aiReading, _aiDockOn;

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
        if (_aiTimer == null)
        {
            _aiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _aiTimer.Tick += (_, _) => _ = RefreshAiUsageAsync();
        }
        else _aiTimer.Interval = TimeSpan.FromSeconds(8); // the first look waits until start-up is over
        _aiTimer.Start();
    }

    void StopAiUsage()
    {
        _aiTimer?.Stop();
        CloseAiCard();
    }

    async Task RefreshAiUsageAsync()
    {
        if (_aiReading || !_settings.AiUsage || _aiTimer == null) return;
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
            if (!_settings.AiUsage) return;
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
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        _aiDock.PointerEntered += (_, _) => HoverAiBubble(true);
        _aiDock.PointerExited += (_, _) => HoverAiBubble(false);
        // Behind the notch: opening, the notch grows over the bubble, and closing uncovers it again.
        _root.Children.Insert(0, _aiDock);
        _aiCardOpen = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _aiCardOpen.Tick += (_, _) => { _aiCardOpen.Stop(); OpenAiCard(); };
        _aiCardClose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        _aiCardClose.Tick += (_, _) => { _aiCardClose.Stop(); CloseAiCard(); };
        _aiCard = new Popup
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = _aiDock,
            VerticalOffset = -4,
            IsLightDismissEnabled = false,
            WindowManagerAddShadowHint = false,
        };
        _root.Children.Add(_aiCard);
    }

    /// <summary>
    /// Whether the bubble shows: beside every closed notch that leaves it room, not beside the open one or a pop-out
    /// that asks something (those take the width).
    /// </summary>
    void ShowAiDock()
    {
        if (_aiDock == null) return;
        double ear = _mini ? 0 : MaxEar;
        bool room = (Math.Max(_shapeW, 0) / 2 + MaxEar + AiDockGap + UsageBubble.Size) <= BaseWidth / 2;
        bool on = _aiUsage.Count > 0 && _aiBubble != null && _settings.AiUsage && !_hiddenForFullscreen && room
                  && _state is not (NotchState.Expanded or NotchState.Share or NotchState.Task or NotchState.Phone);
        if (on) PlaceAiDock(_wSpring.Value, _hSpring.Value, ear);
        if (on == _aiDockOn) return;
        _aiDockOn = on;
        _aiDock.IsHitTestVisible = on;
        if (on)
        {
            _aiDock.IsVisible = true;
            PlaceAiDock(_wSpring.Value, _hSpring.Value, ear);
        }
        else CloseAiCard();

        UpdateHitRegion(_shapeW, _shapeH, _shapeScale);

        if (!_settings.Animations)
        {
            StopTween(_aiDock, "opacity");
            _aiDock.Opacity = on ? 1 : 0;
            if (!on) _aiDock.IsVisible = false;
            return;
        }
        if (on)
            Animate(_aiDock, "opacity", v => _aiDock.Opacity = v, _aiDock.Opacity, 1, 240, 140, QuadOut);
        else
            Animate(_aiDock, "opacity", v => _aiDock.Opacity = v, _aiDock.Opacity, 0, 90, 0, QuadIn,
                () => { if (!_aiDockOn && _aiDock != null) _aiDock.IsVisible = false; });
    }

    /// <summary>
    /// Keeps the bubble at the notch's right shoulder. Runs on every frame the shape moves, so it only updates the
    /// bubble's position in <see cref="_root"/> and resizes it between the minimized and the normal notch.
    /// </summary>
    void PlaceAiDock(double w, double h, double ear)
    {
        if (_aiDock == null || (!_aiDockOn && !_aiDock.IsVisible)) return;
        double grow = Math.Clamp((h - 10) / 14, 0, 1);
        _aiBubble?.SetGrow(grow);
        Canvas.SetLeft(_aiDock, BaseWidth / 2 + w / 2 + ear + AiDockGap);
        Canvas.SetTop(_aiDock, 2 * grow);
    }

    (double X, double Y, double W, double H)? AiDockBounds(double w, double h, double notchScale)
    {
        if (!_aiDockOn || _aiBubble == null) return null;
        double ear = _mini ? 0 : MaxEar;
        double grow = Math.Clamp((h - 10) / 14, 0, 1);
        double leftInRoot = BaseWidth / 2 + w / 2 + ear + AiDockGap;
        double topInRoot = 2 * grow;
        double bw = _aiBubble.Root.Width > 0 ? _aiBubble.Root.Width : UsageBubble.Size;
        double bh = _aiBubble.Root.Height > 0 ? _aiBubble.Root.Height : UsageBubble.Size;
        // _root is horizontally centred in the window and scales around (0.5, 0).
        double x = Width / 2 + (leftInRoot - BaseWidth / 2) * notchScale - 2;
        double y = Math.Max(0, topInRoot * notchScale - 2);
        return (x, y, bw * notchScale + 4, bh * notchScale + 4);
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
        if (_aiDock == null || _aiCard == null || !_aiDockOn || _hiddenForFullscreen || _aiUsage.Count == 0) return;
        _aiCard.Child = AiCard();
        _aiCard.IsOpen = true;
        if (!_settings.Animations || _aiCard.Child is not Control card) return;
        var rise = new TranslateTransform();
        card.RenderTransform = rise;
        Animate(card, "ai-card-rise", v => rise.Y = v, -6, 0, 240, 0, Bouncy);
        Animate(card, "ai-card-fade", v => card.Opacity = v, 0, 1, 140, 0, QuadOut);
    }

    Control AiCard()
    {
        var card = BuildAiCard(_aiUsage);
        card.PointerEntered += (_, _) => _aiCardClose?.Stop();
        card.PointerExited += (_, _) => _aiCardClose?.Start();
        return card;
    }

    void CloseAiCard()
    {
        _aiCardOpen?.Stop();
        _aiCardClose?.Stop();
        if (_aiCard != null) _aiCard.IsOpen = false;
    }

    static readonly IBrush AiCardEdge = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)).ToImmutable();
    static readonly IBrush AiTrack = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)).ToImmutable();
    const double AiCardWidth = 288, AiCardPad = 18;

    /// <summary>The card under the bubble: one calm section per agent, the black of the notch around them.</summary>
    internal static Control BuildAiCard(IReadOnlyList<AiUsageSnapshot> usage)
    {
        var sections = new StackPanel();
        for (int i = 0; i < usage.Count; i++)
        {
            if (i > 0) sections.Children.Add(new Border { Height = 1, Background = HubTheme.Line, Margin = new Thickness(0, 16, 0, 16) });
            sections.Children.Add(AiSection(usage[i]));
        }
        var card = new Border
        {
            Width = AiCardWidth,
            Background = Brushes.Black,
            BorderBrush = AiCardEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(AiCardPad, AiCardPad - 2, AiCardPad, AiCardPad),
            Child = sections,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 28, Color = Color.FromArgb(0x73, 0, 0, 0) }),
        };
        TextElement.SetFontFamily(card, Fonts.Text);
        TextElement.SetForeground(card, Brushes.White);
        // Room for the shadow.
        return new Grid { Width = AiCardWidth + 40, Children = { new Border { Margin = new Thickness(20, 8, 20, 32), Child = card } } };
    }

    static Control AiSection(AiUsageSnapshot usage)
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
            var when = Small("Resets " + LimitReset(resets), HubTheme.Faint);
            when.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(when, Dock.Right);
            head.Children.Add(when);
        }
        head.Children.Add(new TextBlock { Text = UsageBubble.NameOf(usage.Tool), FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        section.Children.Add(head);

        // The number, big and light, and what it means in one line.
        var number = new TextBlock { FontFamily = DisplayFont, Margin = new Thickness(-1, 10, 0, 0), Foreground = Brushes.White };
        number.Inlines!.Add(new Run(usage.Percent == null ? "–" : percent.ToString("0", CultureInfo.InvariantCulture)) { FontSize = 34, FontWeight = FontWeight.Light });
        if (usage.Percent != null) number.Inlines.Add(new Run(" %") { FontSize = 17, FontWeight = FontWeight.Light, Foreground = HubTheme.Dim });
        section.Children.Add(number);
        string meaning = usage.IsLimit
            ? (usage.Limits.FirstOrDefault() is { } first ? $"of the {first.Name}" : "No limits reported yet")
            : usage.TokensInWindow > 0 ? "of your busiest 5 hours this week" : "Nothing in the last 5 hours";
        section.Children.Add(Small(meaning, HubTheme.Dim));

        var fill = new Border
        {
            CornerRadius = new CornerRadius(2), Background = UsageBubble.ColorOf(usage), HorizontalAlignment = HorizontalAlignment.Left,
            Width = percent <= 0 ? 0 : Math.Max(4, inner * percent / 100),
        };
        section.Children.Add(new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 0), Children = { new Border { CornerRadius = new CornerRadius(2), Background = AiTrack }, fill } });

        foreach (var limit in usage.Limits.Skip(1))
        {
            var text = Small(string.Create(CultureInfo.InvariantCulture, $"{limit.UsedPercent:0} % of the {limit.Name}"), HubTheme.Dim);
            text.Margin = new Thickness(0, 10, 0, 0);
            section.Children.Add(text);
            if (limit.ResetsAt is { } reset)
                section.Children.Add(Small("Resets " + LimitReset(reset), HubTheme.Faint));
        }
        if (usage.IsStale)
        {
            var updated = Small("Last reported " + (usage.ReportedAt is { } at ? LimitReset(at) : "earlier") + " · may be outdated", HubTheme.Faint);
            updated.TextWrapping = TextWrapping.Wrap;
            updated.Margin = new Thickness(0, 10, 0, 0);
            section.Children.Add(updated);
        }

        // The sessions at work, one quiet line each.
        foreach (var session in usage.Active.Take(3))
        {
            var row = new DockPanel { Margin = new Thickness(0, section.Children.Count == 4 ? 14 : 7, 0, 0) };
            var dot = new Ellipse { Width = 6, Height = 6, Fill = HubTheme.Green, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 9, 0) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            var project = new TextBlock { Text = session.Project, FontSize = 12.5, FontWeight = FontWeight.SemiBold, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
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

    static TextBlock Small(string text, IBrush brush) => new() { Text = text, FontSize = 12, Foreground = brush, TextTrimming = TextTrimming.CharacterEllipsis };

    internal static string LimitReset(DateTime utc)
        => utc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture);
}
