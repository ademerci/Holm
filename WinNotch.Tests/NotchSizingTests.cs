using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinNotch.Tests;

public class NotchSizingTests
{
    static readonly double[] Scales = { 0.85, 1, 1.15, 1.3 };

    public static IEnumerable<object[]> SizePairs =>
        from collapsed in Scales
        from expanded in Scales
        select new object[] { collapsed, expanded };

    [Theory]
    [MemberData(nameof(SizePairs))]
    public void ExpandingAndCollapsingUseIndependentSizes(double collapsed, double expanded)
    {
        WithWindow(window =>
        {
            var settings = Settings(window);
            settings.CollapsedScale = collapsed;
            settings.ExpandedScale = expanded;
            Invoke(window, "ApplyScale");
            AssertSize(window, collapsed, 150);
            double width = window.Width;

            SetField(window, "_expanded", true);
            Invoke(window, "ApplyState", false);
            AssertSize(window, expanded, 520);
            Assert.Equal(width, window.Width);

            SetField(window, "_expanded", false);
            Invoke(window, "ApplyState", false);
            AssertSize(window, collapsed, 150);
            Assert.Equal(width, window.Width);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingInactiveSizePreservesVisibleSize(bool expanded)
    {
        WithWindow(window =>
        {
            var settings = Settings(window);
            settings.CollapsedScale = settings.ExpandedScale = 1;
            SetField(window, "_expanded", expanded);
            Invoke(window, "ApplyScale");
            var island = (Border)window.FindName("Island");
            double logicalWidth = island.Width;
            double height = window.Height;

            if (expanded) settings.CollapsedScale = 1.3;
            else settings.ExpandedScale = 1.3;
            Invoke(window, "ApplyScale");

            AssertSize(window, 1, logicalWidth);
            Assert.Equal(height, window.Height);
        });
    }

    [Theory]
    [InlineData("_sharing", 400)]
    [InlineData("_taskVisible", 420)]
    [InlineData("_volumePeek", 250)]
    [InlineData("_peekActive", 320)]
    public void PopOutsUseCollapsedSize(string stateField, double logicalWidth)
    {
        WithWindow(window =>
        {
            var settings = Settings(window);
            settings.CollapsedScale = 0.85;
            settings.ExpandedScale = 1.3;
            SetField(window, stateField, true);
            Invoke(window, "ApplyScale");

            AssertSize(window, 0.85, logicalWidth);
        });
    }

    [Fact]
    public void ImmediateSizeChangeSettlesAndCancelsRunningTransition()
    {
        WithWindow(window =>
        {
            Settings(window).Animations = true;
            var settings = Settings(window);
            settings.CollapsedScale = 0.85;
            settings.ExpandedScale = 1.3;
            Invoke(window, "ApplyScale");

            SetField(window, "_expanded", true);
            Invoke(window, "ApplyState", true);
            Assert.True(GetField<bool>(window, "_shapeAnimating"));
            Assert.True(GetField<bool>(window, "_motionRunning"));

            settings.ExpandedScale = 1.15;
            Invoke(window, "ApplyScale");

            AssertSize(window, 1.15, 520);
            Assert.False(GetField<bool>(window, "_shapeAnimating"));
            Assert.False(GetField<bool>(window, "_motionRunning"));
            Assert.False(((ScaleTransform)((Grid)window.FindName("IslandHost")).LayoutTransform).HasAnimatedProperties);
        });
    }

    [Fact]
    public void CollapseReservesRoomForTheOutgoingExpandedView()
    {
        WithWindow(window =>
        {
            Settings(window).Animations = true;
            var settings = Settings(window);
            settings.CollapsedScale = 1.3;
            settings.ExpandedScale = 0.85;
            SetField(window, "_expanded", true);
            Invoke(window, "ApplyScale");
            Layout(window);
            var island = (Border)window.FindName("Island");
            double outgoingLogicalHeight = island.ActualHeight;
            Assert.True(outgoingLogicalHeight > 30);

            SetField(window, "_expanded", false);
            Invoke(window, "ApplyState", true);

            Assert.True(window.Height >= Math.Ceiling(outgoingLogicalHeight * 1.3) + 2);
            Assert.True(GetField<bool>(window, "_shapeAnimating"));
        });
    }

    [Fact]
    public void BrowserUsesTheExistingMiniPillAndRestoresTheIdleView()
    {
        WithWindow(window =>
        {
            var settings = Settings(window);
            settings.CollapsedScale = 1;
            settings.MiniIdle = true;
            Invoke(window, "ApplyState", false);
            var island = (Border)window.FindName("Island");
            var normalPill = (island.Width, island.Height, window.Radius, window.Ear);

            settings.MiniIdle = false;
            SetField(window, "_browserInFront", true);
            Invoke(window, "ApplyState", false);
            Assert.Equal(normalPill, (island.Width, island.Height, window.Radius, window.Ear));
            Assert.Equal(0, ((FrameworkElement)window.FindName("IdleView")).Opacity);

            // Opening takes precedence; collapsing returns to the pill while the browser stays in front.
            SetField(window, "_expanded", true);
            Invoke(window, "ApplyState", false);
            Assert.True(island.Height > 6);
            SetField(window, "_expanded", false);
            Invoke(window, "ApplyState", false);
            Assert.Equal(6, island.Height);

            SetField(window, "_browserInFront", false);
            Invoke(window, "ApplyState", false);
            Assert.Equal(30, island.Height);
            Assert.Equal(1, ((FrameworkElement)window.FindName("IdleView")).Opacity);
        });
    }

    [Fact]
    public void BrowserMinimizesPlayingMusicAndRestoresItAfterwards()
    {
        WithWindow(window =>
        {
            Settings(window).CollapsedScale = 1;
            SetField(window, "_info", new MediaInfo { IsPlaying = true });
            SetField(window, "_browserInFront", true);
            Invoke(window, "ApplyState", false);
            AssertSize(window, 1, 96);
            Assert.Equal(0, ((FrameworkElement)window.FindName("CompactView")).Opacity);

            SetField(window, "_browserInFront", false);
            Invoke(window, "ApplyState", false);
            AssertSize(window, 1, 220);
            Assert.Equal(1, ((FrameworkElement)window.FindName("CompactView")).Opacity);
        });
    }

    [Theory]
    [InlineData("_sharing", 400)]
    [InlineData("_taskVisible", 420)]
    [InlineData("_volumePeek", 250)]
    [InlineData("_peekActive", 320)]
    public void BrowserPillStillAllowsAlerts(string stateField, double logicalWidth)
    {
        WithWindow(window =>
        {
            Settings(window).CollapsedScale = 1;
            SetField(window, "_browserInFront", true);
            SetField(window, stateField, true);
            Invoke(window, "ApplyState", false);
            AssertSize(window, 1, logicalWidth);

            SetField(window, stateField, false);
            Invoke(window, "ApplyState", false);
            AssertSize(window, 1, 96);
        });
    }

    static void AssertSize(MainWindow window, double expectedScale, double logicalWidth)
    {
        var transform = (ScaleTransform)((Grid)window.FindName("IslandHost")).LayoutTransform;
        var island = (Border)window.FindName("Island");
        Assert.Equal(expectedScale, transform.ScaleX, 8);
        Assert.Equal(expectedScale, transform.ScaleY, 8);
        Assert.Equal(logicalWidth, island.Width, 8);
        Assert.Equal(Math.Ceiling(island.Height * expectedScale) + 2, window.Height);
        Assert.True(window.Width >= (logicalWidth + 20) * expectedScale);
    }

    static void Layout(MainWindow window)
    {
        var root = (Grid)window.FindName("Root");
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        root.UpdateLayout();
    }

    [Fact]
    public void CtrlHOpensTheNotchAndClosesItAgain()
    {
        Assert.True(new AppSettings().HotKey); // on by default
        bool readOnly = AppSettings.ReadOnly;
        AppSettings.ReadOnly = true; // opening counts toward achievements, which must not touch your files
        try
        {
            WithWindow(window =>
            {
                window.ToggleFromKeyboard();
                Assert.True(GetField<bool>(window, "_expanded"));
                window.ToggleFromKeyboard();
                Assert.False(GetField<bool>(window, "_expanded"));
            });
        }
        finally { AppSettings.ReadOnly = readOnly; }
    }

    [Theory]
    [InlineData(true, 1.0)]
    [InlineData(false, 0.0)]
    public void TheSkyColoursCanBeSwitchedOff(bool colours, double opacity)
    {
        Assert.True(new AppSettings().SkyColors); // on by default
        WithWindow(window =>
        {
            Settings(window).SkyColors = colours;
            SetField(window, "_expanded", true);
            Invoke(window, "ShowSky");
            Assert.Equal(opacity, ((Border)window.FindName("SkyLayer")).Opacity);
        });
    }

    [Fact]
    public void AiRingsSitInsideTheClosedNotchAndWidenIt()
    {
        WithWindow(window =>
        {
            var now = DateTime.UtcNow;
            var claude = new AiUsageSnapshot(AiTool.ClaudeCode, 73, "", now.AddMinutes(51), 0,
                new[] { new AiSession(AiTool.ClaudeCode, "holm", "Bash: Run the tests", now) }) { IsLimit = true };
            var codex = new AiUsageSnapshot(AiTool.Codex, 21, "", null, 0, Array.Empty<AiSession>()) { IsLimit = true };
            Invoke(window, "ApplyState", false);
            double plain = window.Width > 0 ? GetField<double>(window, "_shapeW") : 0;

            // Both tools: Claude left, Codex right, and the notch grows by both wings.
            window.ShowAiUsage(new[] { codex, claude });
            Invoke(window, "ApplyState", false);
            Assert.Equal((true, true), window.AiWingsShown);
            Assert.Equal(plain + 2 * MainWindow.AiWing, GetField<double>(window, "_shapeW"));

            // One tool sits on the right only.
            window.ShowAiUsage(new[] { codex });
            Invoke(window, "ApplyState", false);
            Assert.Equal((false, true), window.AiWingsShown);
            Assert.Equal(plain + MainWindow.AiWing, GetField<double>(window, "_shapeW"));

            // Not beside the minimized pill, not in the open notch, and gone with nothing to show.
            window.ShowAiUsage(new[] { codex, claude });
            Settings(window).MiniIdle = true;
            Invoke(window, "ApplyState", false);
            Assert.Equal((false, false), window.AiWingsShown);
            Settings(window).MiniIdle = false;
            SetField(window, "_expanded", true);
            Invoke(window, "ApplyState", false);
            Assert.Equal((false, false), window.AiWingsShown);
            SetField(window, "_expanded", false);
            window.ShowAiUsage(Array.Empty<AiUsageSnapshot>());
            Assert.Equal((false, false), window.AiWingsShown);
            Assert.Equal(plain, GetField<double>(window, "_shapeW"));

            // Codenotch's colours: green, yellow from 50 %, red from 70 %.
            Assert.Same(UsageRing.Ample, UsageRing.ColorOf(21));
            Assert.Same(UsageRing.Watch, UsageRing.ColorOf(52));
            Assert.Same(UsageRing.Critical, UsageRing.ColorOf(73));
        });
    }

    [Fact]
    public void AiPageShowsEveryWindowWithItsReset()
    {
        WithWindow(_ =>
        {
            var now = DateTime.UtcNow;
            var claude = new AiUsageSnapshot(AiTool.ClaudeCode, 73, "", now.AddMinutes(51), 0, Array.Empty<AiSession>())
            {
                IsLimit = true, Limits = new[]
                {
                    new AiUsageLimit(73, 300, now.AddMinutes(51)) { Label = "Current session" },
                    new AiUsageLimit(7, 10080, now.AddDays(3)) { Label = "Weekly · all models" },
                },
            };
            var stale = new AiUsageSnapshot(AiTool.Codex, 31, "", now.AddDays(2), 0, Array.Empty<AiSession>())
            {
                IsLimit = true, IsStale = true, ReportedAt = now.AddHours(-1), Limits = new[] { new AiUsageLimit(31, 10080, now.AddDays(2)) },
            };
            var estimate = new AiUsageSnapshot(AiTool.ClaudeCode, 40, "Not signed in to Claude Code", now.AddHours(2), 800_000, Array.Empty<AiSession>());
            static IEnumerable<string> Texts(DependencyObject node)
            {
                if (node is TextBlock text) yield return text.Text;
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                    foreach (var value in Texts(child)) yield return value;
            }
            var texts = Texts(MainWindow.BuildAiPage(new[] { claude, stale }, now)).ToArray();
            Assert.Contains("Current session", texts);
            Assert.Contains("Weekly · all models", texts);
            Assert.Contains("Resets in 51 min", texts);
            Assert.Contains("73% used", texts);
            Assert.Contains("Weekly limit", texts);
            Assert.Contains(texts, text => text.Contains("may be outdated"));

            texts = Texts(MainWindow.BuildAiPage(new[] { estimate }, now)).ToArray();
            Assert.Contains("Estimate · 5 hours", texts);
            Assert.Contains("Not signed in to Claude Code", texts);

            var page = MainWindow.BuildAiPage(new[] { claude, stale }, now);
            page.Measure(new Size(420, double.PositiveInfinity));
            Assert.InRange(page.DesiredSize.Height, 80, 260);
        });
    }

    [Fact]
    public void ClaudeLimitsReadTheReplyWithoutTwins()
    {
        using var json = System.Text.Json.JsonDocument.Parse("""
            {"limits":[{"kind":"session","percent":73,"resets_at":"2026-10-10T00:00:00Z"},
                       {"kind":"weekly_all","percent":7,"resets_at":"2026-10-15T00:00:00Z"},
                       {"kind":"weekly_opus","percent":12}],
             "five_hour":{"utilization":73,"resets_at":"2026-10-10T00:00:00Z"},
             "seven_day":{"utilization":7,"resets_at":"2026-10-15T00:00:00Z"}}
            """);
        var limits = ClaudeLimits.Parse(json.RootElement);
        Assert.Equal(new[] { "Current session", "Weekly · all models" }, limits.Select(l => l.Name)); // no reset, not shown
        Assert.Equal((73.0, 300), (limits[0].UsedPercent, limits[0].WindowMinutes.GetValueOrDefault()));

        // A window that just rolled over is missing from limits: the named field fills it in.
        using var rolled = System.Text.Json.JsonDocument.Parse("""{"limits":[],"five_hour":{"utilization":4,"resets_at":"2026-10-10T05:00:00Z"}}""");
        Assert.Equal("Current session", Assert.Single(ClaudeLimits.Parse(rolled.RootElement)).Name);

        // Live limits replace the log estimate; a problem without limits stays on the estimate.
        var logs = new AiUsageSnapshot(AiTool.ClaudeCode, 40, "", null, 800_000, Array.Empty<AiSession>());
        var live = MainWindow.WithLiveLimits(logs, limits, DateTime.UtcNow, false, null)!;
        Assert.True(live.IsLimit);
        Assert.Equal(73, live.Percent);
        Assert.Equal(7, live.WeekPercent);
        Assert.Equal("Offline", MainWindow.WithLiveLimits(logs, Array.Empty<AiUsageLimit>(), null, true, "Offline")!.Label);
    }

    [Fact]
    public void PositionResetActionSitsNextToTheSwitchWithoutTogglingIt()
    {
        WithWindow(_ =>
        {
            int resets = 0;
            var item = new MenuItem { Name = "MenuDraggable", Header = "Drag notch position", IsCheckable = true };
            var setting = new SettingsSwitch(item, "", "Move along the top edge", "Reset Position", () => resets++);
            var group = new SettingsGroup("general", "General", "", Colors.Gray, "", new[] { setting });
            var hub = new SettingsWindow(new[] { group }, "test", false, () => { });
            try
            {
                var row = (FrameworkElement)typeof(SettingsWindow).GetMethod("Row", PrivateInstance)!
                    .Invoke(hub, new object[] { setting, Colors.Gray, true, true })!;
                static IEnumerable<DependencyObject> Children(DependencyObject node)
                {
                    yield return node;
                    foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                        foreach (var descendant in Children(child)) yield return descendant;
                }
                var reset = Assert.Single(Children(row).OfType<Button>());
                Assert.Equal("Reset Position", reset.Content);
                var toggle = Assert.Single(Children(row).OfType<ToggleSwitch>());
                Assert.False(toggle.IsOn);
                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, reset));
                Assert.Equal(1, resets);
                Assert.False(toggle.IsOn);
                row.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                    System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Assert.True(toggle.IsOn);
                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, reset));
                Assert.Equal(2, resets);
                Assert.True(toggle.IsOn);
            }
            finally { hub.Close(); }
        });
    }

    // Never show the window: Loaded starts the real media, notification and sharing services.
    // Settings are changed only in memory, and no menu click or Save method is invoked.
    static void WithWindow(Action<MainWindow> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                var settings = Settings(window);
                settings.MiniIdle = false;
                settings.Animations = false;
                test(window);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The hidden notch sizing check did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static AppSettings Settings(MainWindow window) => GetField<AppSettings>(window, "_settings");
    static T GetField<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)!;
    static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(window, value);
    static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(window, args);
}
