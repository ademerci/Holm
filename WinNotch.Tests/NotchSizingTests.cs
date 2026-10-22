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
            AssertSize(window, expanded, 440);
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
            var settings = Settings(window);
            settings.CollapsedScale = 0.85;
            settings.ExpandedScale = 1.3;
            Invoke(window, "ApplyScale");

            SetField(window, "_expanded", true);
            Invoke(window, "ApplyState", true);
            Assert.True(GetField<bool>(window, "_shapeAnimating"));
            Assert.True(GetField<DispatcherTimer>(window, "_fitTimer").IsEnabled);

            settings.ExpandedScale = 1.15;
            Invoke(window, "ApplyScale");

            AssertSize(window, 1.15, 440);
            Assert.False(GetField<bool>(window, "_shapeAnimating"));
            Assert.False(GetField<DispatcherTimer>(window, "_fitTimer").IsEnabled);
            Assert.False(((ScaleTransform)((Grid)window.FindName("IslandHost")).LayoutTransform).HasAnimatedProperties);
        });
    }

    [Fact]
    public void CollapseReservesRoomForTheOutgoingExpandedView()
    {
        WithWindow(window =>
        {
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
