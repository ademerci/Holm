using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinNotch;

namespace WinNotch.Tests;

/// <summary>
/// On a real desktop: a volume key pressed while Holm holds the keys reaches Holm and never Windows, so Windows changes
/// nothing and opens no pop-up. The volume is put back if it moved.
/// </summary>
[Trait("Category", "Local")]
public class MediaKeysLiveTests
{
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    [Fact]
    public void A_volume_key_goes_to_holm_and_not_to_windows()
    {
        var volume = new VolumeService();
        volume.Bind();
        if (!volume.TryGet(out float before, out bool muted)) return; // no speakers
        var pressed = new List<int>();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using var keys = new MediaKeys(dispatcher, _ => true, pressed.Add);
                    keybd_event(MediaKeys.VolumeUp, 0, 0, IntPtr.Zero);
                    keybd_event(MediaKeys.VolumeUp, 0, 2 /* KEYEVENTF_KEYUP */, IntPtr.Zero);
                    for (int i = 0; i < 20 && pressed.Count == 0; i++) await Task.Delay(50);
                    await Task.Delay(400);
                }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        volume.TryGet(out float after, out bool mutedAfter);
        if (Math.Abs(after - before) > 0.001f || mutedAfter != muted) { volume.SetLevel(before); volume.SetMute(muted); }
        volume.Shutdown();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Equal(new[] { (int)MediaKeys.VolumeUp }, pressed); // once: the key going up isn't a press
        Assert.Equal(before, after, 3); // Windows never saw it
        Assert.Equal(muted, mutedAfter);
    }
}
