using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinNotch;

namespace WinNotch.Tests;

/// <summary>
/// With the real Handy on this PC (installed and running): starts dictation, checks that Holm sees it and moves Handy's
/// overlay off screen, then cancels (nothing is transcribed or typed). Skips without Handy.
/// </summary>
[Trait("Category", "Local")]
public class HandyLiveTests
{
    [Fact]
    public void Holm_sees_handy_dictate_and_keeps_its_overlay_out_of_sight()
    {
        if (HandyBridge.FindInstall() is not { } exe || !Process.GetProcessesByName("handy").Any()) return;
        var seen = new List<bool>();
        (int Left, int Top)? whileDictating = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using var bridge = new HandyBridge(dispatcher) { HideOverlay = true };
                    bridge.DictatingChanged += on => seen.Add(on);
                    Process.Start(exe, "--toggle-transcription")!.WaitForExit();
                    for (int i = 0; i < 40 && !seen.Contains(true); i++) await Task.Delay(100);
                    await Task.Delay(300);
                    var overlay = FindWindow("Tauri Window", "Recording");
                    if (GetWindowRect(overlay, out var rect)) whileDictating = (rect.Left, rect.Top);
                    Process.Start(exe, "--cancel")!.WaitForExit();
                    for (int i = 0; i < 40 && !seen.Contains(false); i++) await Task.Delay(100);
                }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Equal(new[] { true, false }, seen);
        Assert.NotNull(whileDictating);
        Assert.True(whileDictating!.Value.Left < -10000 && whileDictating.Value.Top < -10000, $"overlay at {whileDictating}");
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
