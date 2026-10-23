using System.Collections.Concurrent;
using System.Windows.Threading;

namespace WinNotch.Tests;

public class ScreenshotWatcherTests
{
    /// <summary>Runs a watcher on its own UI thread, as the notch does, and collects what it reports.</summary>
    sealed class Running : IDisposable
    {
        readonly Thread _thread;
        Dispatcher? _ui;
        ScreenshotWatcher? _watcher;
        public readonly BlockingCollection<string> Taken = new();

        public Running(string folder)
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _ui = Dispatcher.CurrentDispatcher;
                _watcher = new ScreenshotWatcher(folder, _ui);
                _watcher.Taken += Taken.Add;
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        }

        public void Dispose()
        {
            _ui?.Invoke(() => _watcher?.Dispose());
            _ui?.InvokeShutdown();
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void A_new_screenshot_is_reported_once_it_is_written()
    {
        using var folder = new TempFolder();
        using var watcher = new Running(folder.Path);

        string shot = Path.Combine(folder.Path, "Screenshot 2026-10-23 154100.png");
        using (var file = File.Create(shot))
        {
            file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            Thread.Sleep(150); // still being written
            file.Write(new byte[2048]);
        }

        Assert.True(watcher.Taken.TryTake(out var path, TimeSpan.FromSeconds(5)), "The screenshot wasn't reported.");
        Assert.Equal(shot, path);
        Assert.False(watcher.Taken.TryTake(out _, TimeSpan.FromMilliseconds(800)), "Reported twice.");
    }

    [Fact]
    public void A_screenshot_saved_under_a_temporary_name_counts_when_renamed()
    {
        using var folder = new TempFolder();
        using var watcher = new Running(folder.Path);
        string temp = Path.Combine(folder.Path, "~shot.tmp"), shot = Path.Combine(folder.Path, "Screenshot.png");
        File.WriteAllBytes(temp, new byte[512]);
        File.Move(temp, shot);
        Assert.True(watcher.Taken.TryTake(out var path, TimeSpan.FromSeconds(5)));
        Assert.Equal(shot, path);
    }

    [Fact]
    public void Other_files_are_ignored()
    {
        using var folder = new TempFolder();
        using var watcher = new Running(folder.Path);
        File.WriteAllBytes(Path.Combine(folder.Path, "notes.txt"), new byte[10]);
        File.WriteAllBytes(Path.Combine(folder.Path, "clip.mp4"), new byte[10]);
        Assert.False(watcher.Taken.TryTake(out _, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void An_empty_file_is_never_reported()
    {
        using var folder = new TempFolder();
        using var watcher = new Running(folder.Path);
        File.WriteAllBytes(Path.Combine(folder.Path, "empty.png"), Array.Empty<byte>());
        Assert.False(watcher.Taken.TryTake(out _, TimeSpan.FromSeconds(1.5)));
    }

    [Fact, Trait("Category", "Local")]
    public void The_screenshots_folder_is_found()
    {
        string? folder = Native.ScreenshotsFolder();
        Assert.False(string.IsNullOrEmpty(folder));
        Assert.EndsWith("Screenshots", folder);
    }
}
