using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using WinNotch;

namespace WinNotch.Tests;

/// <summary>While Handy dictates, the closed notch says so, over a message badge or music.</summary>
public class DictationStateTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void Dictating_shows_in_the_closed_notch_and_goes_when_it_stops()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(() =>
            {
                MainWindow? window = null;
                try
                {
                    window = new MainWindow();
                    var settings = (AppSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(window)!;
                    settings.Animations = false;
                    var onDictating = typeof(MainWindow).GetMethod("OnDictating", Private)!;
                    string State() => typeof(MainWindow).GetField("_state", Private)!.GetValue(window)!.ToString()!;
                    onDictating.Invoke(window, new object[] { true });
                    Assert.Equal("Dictation", State());
                    Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("DictationView")).Visibility);
                    onDictating.Invoke(window, new object[] { false });
                    Assert.NotEqual("Dictation", State());
                }
                catch (Exception ex) { failure = ex; }
                finally { window?.Close(); dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
