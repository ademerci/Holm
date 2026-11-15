using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WinNotch.Tests;

/// <summary>Opening a tool you type into hands its field the keyboard, yet the notch still closes if you just look.</summary>
public class ToolFocusTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Opening_a_typing_tool_focuses_its_field_without_holding_the_notch_open()
        => WithWindow(window =>
        {
            Toggle(window, "Notes");
            Assert.True(Field<bool>(window, "_typingUntouched"));
            Set(window, "_typing", true); // what activation does when the window has a handle
            Assert.False(KeepOpen(window)); // only looked: leaving with the mouse closes the notch

            var box = (TextBox)window.FindName("NotesBox");
            using (var source = new HwndSource(new HwndSourceParameters("test")))
                box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.A) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.True(KeepOpen(window)); // typed: the notch stays while you type

            Toggle(window, "Notes"); // back home ends typing
            Assert.False(Field<bool>(window, "_typingUntouched"));
            Assert.False(KeepOpen(window));
        });

    [Fact]
    public void Tools_without_a_field_do_not_take_the_keyboard()
        => WithWindow(window =>
        {
            Toggle(window, "Clipboard");
            Assert.False(Field<bool>(window, "_typingUntouched"));
            Toggle(window, "Calculator");
            Assert.True(Field<bool>(window, "_typingUntouched"));
        });

    static void Toggle(MainWindow window, string tool)
    {
        var type = typeof(MainWindow).GetNestedType("Tool", BindingFlags.NonPublic)!;
        typeof(MainWindow).GetMethod("ToggleTool", Private)!.Invoke(window, new[] { Enum.Parse(type, tool) });
    }

    static bool KeepOpen(MainWindow window) => (bool)typeof(MainWindow).GetProperty("KeepOpenForTyping", Private)!.GetValue(window)!;
    static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    static void Set(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);

    static void WithWindow(Action<MainWindow> test)
    {
        Exception? failure = null;
        bool readOnly = AppSettings.ReadOnly;
        AppSettings.ReadOnly = true; // ending typing saves the note: never into the real settings
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
        bool done = thread.Join(TimeSpan.FromSeconds(20));
        AppSettings.ReadOnly = readOnly;
        Assert.True(done, "The tool focus check did not finish.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
