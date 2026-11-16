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
    public void TheAiBubbleSitsBesideTheClosedNotchAndFlattensBesideTheMiniPill()
    {
        WithWindow(window =>
        {
            var claude = new AiUsageSnapshot(AiTool.ClaudeCode, 62, "", DateTime.UtcNow.AddHours(2), 1_240_000,
                new[] { new AiSession(AiTool.ClaudeCode, "holm", "Bash: Run the tests", DateTime.UtcNow) }) { BusiestTokens = 2_000_000 };
            var codex = new AiUsageSnapshot(AiTool.Codex, 95, "", null, 0, Array.Empty<AiSession>()) { IsLimit = true, WeekPercent = 40 };
            Invoke(window, "ApplyState", false);
            window.ShowAiUsage(new[] { codex, claude });

            // One bubble: the agent at work wins over the one further along.
            Assert.True(window.AiDockShown);
            Assert.Equal(AiTool.ClaudeCode, window.AiBubble!.Shown.Tool);
            Assert.Equal((UsageBubble.Size, UsageBubble.Size), (window.AiBubble.Root.Width, window.AiBubble.Root.Height));
            Assert.Equal(AiTool.Codex, MainWindow.Leading(new[] { claude with { Active = Array.Empty<AiSession>() }, codex })!.Tool);
            Assert.Same(HubTheme.Red, UsageBubble.ColorOf(codex)); // nearly used up

            // Beside the minimized notch it is a capsule no taller than it, so the window needn't grow.
            Settings(window).MiniIdle = true;
            Invoke(window, "ApplyState", false);
            Assert.True(window.AiDockShown);
            Assert.Equal(UsageBubble.MiniHeight, window.AiBubble.Root.Height);
            Assert.True(window.Height <= 10);

            // The open notch takes the room; with nothing to show it's gone.
            SetField(window, "_expanded", true);
            Invoke(window, "ApplyState", false);
            Assert.False(window.AiDockShown);
            SetField(window, "_expanded", false);
            Invoke(window, "ApplyState", false);
            Assert.True(window.AiDockShown);
            window.ShowAiUsage(Array.Empty<AiUsageSnapshot>());
            Assert.False(window.AiDockShown);

            // The card lays out with one agent and with both.
            foreach (var usage in new[] { new[] { codex }, new[] { claude, codex } })
            {
                var card = MainWindow.BuildAiCard(usage);
                card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.InRange(card.DesiredSize.Height, 120, 460);
            }
        });
    }

    [Fact]
    public void AiCardUsesReportedWindowsAndShowsStaleReadings()
    {
        WithWindow(_ =>
        {
            var now = DateTime.UtcNow;
            var weekly = new AiUsageSnapshot(AiTool.Codex, 31, "", now.AddDays(2), 0, Array.Empty<AiSession>())
            {
                IsLimit = true, IsStale = true, ReportedAt = now.AddHours(-1),
                Limits = new[] { new AiUsageLimit(31, 10080, now.AddDays(2)) },
            };
            static IEnumerable<string> Texts(DependencyObject node)
            {
                if (node is TextBlock text) yield return text.Text;
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                    foreach (var value in Texts(child)) yield return value;
            }
            var texts = Texts(MainWindow.BuildAiCard(new[] { weekly })).ToArray();
            Assert.Contains("of the weekly limit", texts);
            Assert.DoesNotContain(texts, text => text.Contains("5-hour"));
            Assert.Contains("Resets " + MainWindow.LimitReset(now.AddDays(2)), texts);
            Assert.Contains(texts, text => text.Contains("may be outdated"));

            var both = weekly with { IsStale = false, Percent = 10, Limits = new[]
            {
                new AiUsageLimit(10, 300, now.AddHours(1)),
                new AiUsageLimit(31, 10080, now.AddDays(2)),
            } };
            texts = Texts(MainWindow.BuildAiCard(new[] { both })).ToArray();
            Assert.Contains("of the 5-hour limit", texts);
            Assert.Contains("31 % of the weekly limit", texts);
            Assert.DoesNotContain(texts, text => text.Contains("may be outdated"));
            texts = Texts(MainWindow.BuildAiCard(new[] { weekly with
            {
                Percent = null, ResetsAt = null, Limits = Array.Empty<AiUsageLimit>(), IsStale = false,
            } })).ToArray();
            Assert.Contains("No limits reported yet", texts);
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
