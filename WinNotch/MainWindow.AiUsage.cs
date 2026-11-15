using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinNotch;

// Everything is read from this PC's own log files, shown only here, never sent anywhere and never shared with friends.
public partial class MainWindow
{
    Grid? _aiRing;
    System.Windows.Shapes.Path? _aiArc;
    Ellipse? _aiTrack;
    TextBlock? _aiGlyph;
    DispatcherTimer? _aiTimer;
    AiUsageSnapshot? _aiClaude;
    AiUsageSnapshot? _aiCodex;
    bool _aiReading;

    void StartAiUsage()
    {
        StopAiUsage();
        if (!_settings.AiUsage)
        {
            if (_aiRing != null)
            {
                _aiRing.Visibility = Visibility.Collapsed;
            }
            return;
        }

        EnsureAiRing();
        _aiTimer ??= MakeTimer(60_000, () => _ = RefreshAiUsageAsync());
        _aiTimer.Start();
        _ = RefreshAiUsageAsync();
    }

    void StopAiUsage()
    {
        _aiTimer?.Stop();
        if (_aiRing != null)
        {
            _aiRing.Visibility = Visibility.Collapsed;
        }
    }

    void EnsureAiRing()
    {
        if (_aiRing != null) return;

        _aiRing = new Grid
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Arrow,
            Visibility = Visibility.Collapsed
        };

        _aiTrack = new Ellipse
        {
            Width = 24,
            Height = 24,
            Stroke = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 2.5
        };

        _aiArc = new System.Windows.Shapes.Path
        {
            Stroke = Brushes.White,
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        _aiGlyph = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White
        };

        _aiRing.Children.Add(_aiTrack);
        _aiRing.Children.Add(_aiArc);
        _aiRing.Children.Add(_aiGlyph);

        var toolTip = new ToolTip
        {
            Placement = PlacementMode.Bottom,
            HasDropShadow = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetInitialShowDelay(_aiRing, 120);
        ToolTipService.SetShowDuration(_aiRing, 60000);
        _aiRing.ToolTip = toolTip;

        _aiRing.ToolTipOpening += (s, e) =>
        {
            if (_aiRing.ToolTip is ToolTip tip)
            {
                tip.Content = BuildAiCard();
            }
        };

        Panel.SetZIndex(_aiRing, 50);
        Root.Children.Add(_aiRing);
    }

    async Task RefreshAiUsageAsync()
    {
        if (_aiReading) return;
        _aiReading = true;

        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var result = await Task.Run(() =>
            {
                var claudeDir = System.IO.Path.Combine(home, ".claude");
                var codexDir = System.IO.Path.Combine(home, ".codex");
                var now = DateTime.UtcNow;
                var c1 = AiUsage.ReadClaudeCode(claudeDir, now);
                var c2 = AiUsage.ReadCodex(codexDir, now);
                return (c1, c2);
            });

            _aiClaude = result.c1;
            _aiCodex = result.c2;
        }
        catch
        {
            // Keep old values on error
        }
        finally
        {
            _aiReading = false;
        }

        ShowAiRing();
    }

    void ShowAiRing()
    {
        EnsureAiRing();
        if (_aiRing == null || _aiArc == null || _aiGlyph == null) return;

        AiUsageSnapshot? activeSnapshot = null;
        if (_aiClaude?.Active.Count > 0) activeSnapshot = _aiClaude;
        else if (_aiCodex?.Active.Count > 0) activeSnapshot = _aiCodex;
        else
        {
            double pClaude = _aiClaude?.Percent ?? -1;
            double pCodex = _aiCodex?.Percent ?? -1;
            if (pClaude >= pCodex && _aiClaude != null) activeSnapshot = _aiClaude;
            else if (_aiCodex != null) activeSnapshot = _aiCodex;
            else activeSnapshot = _aiClaude;
        }

        if (!_settings.AiUsage || activeSnapshot == null || (_aiClaude == null && _aiCodex == null))
        {
            if (_aiRing.Visibility != Visibility.Collapsed)
            {
                _aiRing.Visibility = Visibility.Collapsed;
            }
            return;
        }

        bool isClaude = activeSnapshot.Tool == AiTool.ClaudeCode;
        if (isClaude)
        {
            _aiGlyph.Text = "✳";
            _aiGlyph.FontSize = 11;
            _aiGlyph.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97757"));
        }
        else
        {
            _aiGlyph.Text = ">_";
            _aiGlyph.FontSize = 9;
            _aiGlyph.Foreground = Brushes.White;
        }

        double pct = activeSnapshot.Percent ?? 0;
        if (pct >= 99.9)
        {
            _aiArc.Data = new EllipseGeometry(new Point(12, 12), 10.75, 10.75);
        }
        else if (pct <= 0)
        {
            _aiArc.Data = null;
        }
        else
        {
            double angle = Math.Clamp(pct, 0, 100) / 100.0 * 360.0;
            double rad = angle * Math.PI / 180.0;
            double cx = 12, cy = 12, r = 10.75;
            double startX = cx, startY = cy - r;
            double endX = cx + r * Math.Sin(rad);
            double endY = cy - r * Math.Cos(rad);
            bool isLarge = angle > 180;

            var fig = new PathFigure { StartPoint = new Point(startX, startY), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(endX, endY), new Size(r, r), 0, isLarge, SweepDirection.Clockwise, true));
            var geom = new PathGeometry();
            geom.Figures.Add(fig);
            _aiArc.Data = geom;
        }

        Color strokeColor;
        if (pct >= 90)
        {
            strokeColor = (Color)ColorConverter.ConvertFromString("#FF453A");
        }
        else if (isClaude)
        {
            strokeColor = (Color)ColorConverter.ConvertFromString("#D97757");
        }
        else
        {
            strokeColor = (Color)ColorConverter.ConvertFromString("#10A37F");
        }
        _aiArc.Stroke = new SolidColorBrush(strokeColor);

        PlaceAiRing();
        if (!_shapeAnimating) FitWindowHeight(); // room for the ring, or back to the notch's own height
    }

    /// <summary>The ring has something to show (the window keeps room for it).</summary>
    bool AiRingShown => _settings.AiUsage && (_aiClaude != null || _aiCodex != null);

    void PlaceAiRing()
    {
        if (_aiRing == null) return;

        bool shouldShow = _settings.AiUsage && (_aiClaude != null || _aiCodex != null) && !_expanded && !_hiddenForFullscreen && Root.ActualWidth > 0;
        Visibility targetVis = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        if (_aiRing.Visibility != targetVis)
        {
            _aiRing.Visibility = targetVis;
        }

        if (!shouldShow) return;

        double scale = _notchScale.ScaleX;
        double right = Root.ActualWidth / 2 + (Island.ActualWidth / 2 + MaxEar) * scale;
        _aiRing.Margin = new Thickness(right + 6, Math.Max(2, (Island.ActualHeight * scale - 24) / 2), 0, 0);
    }

    UIElement BuildAiCard()
    {
        var stack = new StackPanel { Width = 300 };

        var snapshots = new List<AiUsageSnapshot>();
        if (_aiClaude != null) snapshots.Add(_aiClaude);
        if (_aiCodex != null) snapshots.Add(_aiCodex);

        for (int i = 0; i < snapshots.Count; i++)
        {
            var snap = snapshots[i];
            if (i > 0)
            {
                stack.Children.Add(new FrameworkElement { Height = 10 });
            }

            string toolTitle = snap.Tool == AiTool.ClaudeCode ? "Claude Code" : "Codex";
            stack.Children.Add(new TextBlock
            {
                Text = toolTitle,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = Brushes.White
            });

            if (!string.IsNullOrEmpty(snap.Label))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = snap.Label,
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCFFFFFF")),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (snap.ResetsAt.HasValue)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"Resets at {snap.ResetsAt.Value.ToLocalTime():HH:mm}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#99FFFFFF")),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = $"{snap.TokensInWindow:N0} tokens in this window",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#99FFFFFF")),
                Margin = new Thickness(0, 2, 0, 0)
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Working now",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 6, 0, 2)
            });

            if (snap.Active.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No agent working right now",
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#99FFFFFF"))
                });
            }
            else
            {
                foreach (var session in snap.Active)
                {
                    string stepText = string.IsNullOrEmpty(session.Step) ? "working" : session.Step;
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"{session.Project} · {stepText}",
                        FontSize = 11,
                        Foreground = Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
            }
        }

        return new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F21C1C1E")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = stack
        };
    }

    void MenuAiUsage_Click(object sender, RoutedEventArgs e)
    {
        _settings.AiUsage = MenuAiUsage.IsChecked;
        _settings.Save();
        StartAiUsage();
    }
}