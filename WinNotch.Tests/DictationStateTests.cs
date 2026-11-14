using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using WinNotch;
using WinNotch.Notify;

namespace WinNotch.Tests;

/// <summary>While Handy dictates, the closed notch says so, over a message badge or music.</summary>
public class DictationStateTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void Another_apps_notification_pops_out_at_the_top_when_the_switch_is_on()
        => OnWindow(window =>
        {
            var settings = (AppSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(window)!;
            settings.Animations = false;
            settings.QuietHours = false;
            var pop = typeof(MainWindow).GetMethod("OnOtherNotification", Private)!;
            string State() => typeof(MainWindow).GetField("_state", Private)!.GetValue(window)!.ToString()!;
            var note = new ChatMessage { App = "Microsoft.Teams", Title = "Standup in 5 minutes", Text = "Room 2", Time = DateTimeOffset.Now };
            settings.AllNotifications = false;
            pop.Invoke(window, new object[] { note });
            Assert.NotEqual("Message", State());
            settings.AllNotifications = true;
            pop.Invoke(window, new object[] { note });
            Assert.Equal("Message", State());
            Assert.Equal("Standup in 5 minutes", ((System.Windows.Controls.TextBlock)window.FindName("MessageTitle")).Text);
        });

    static void OnWindow(Action<MainWindow> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(() =>
            {
                MainWindow? window = null;
                try { window = new MainWindow(); test(window); }
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
