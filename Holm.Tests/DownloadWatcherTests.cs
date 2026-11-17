using WinNotch.Notify;

namespace WinNotch.Tests;

/// <summary>Plays the browsers' file dance in a temp folder: create, grow, rename, delete.</summary>
public class DownloadWatcherTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    sealed class Recorder : IDisposable
    {
        public readonly DownloadWatcher Watcher;
        public readonly List<string> Log = new();
        readonly List<(Func<bool> Done, TaskCompletionSource Signal)> _waits = new();

        public Recorder(string folder)
        {
            Watcher = new DownloadWatcher(folder, TimeSpan.FromMilliseconds(100));
            Watcher.Started += d => Add($"started {d.Name}");
            Watcher.Updated += d => Add($"updated {d.Name} {d.Bytes}");
            Watcher.Finished += (d, file) => Add($"finished {d.Name} {Path.GetFileName(file)} {d.Bytes}");
            Watcher.Cancelled += d => Add($"cancelled {d.Name}");
            Watcher.Start();
        }

        void Add(string entry)
        {
            lock (Log)
            {
                Log.Add(entry);
                foreach (var (done, signal) in _waits.Where(w => w.Done()).ToList()) signal.TrySetResult();
            }
        }

        public async Task Until(Func<IReadOnlyList<string>, bool> condition)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Log)
            {
                if (condition(Log)) return;
                _waits.Add((() => condition(Log), signal));
            }
            var finished = await Task.WhenAny(signal.Task, Task.Delay(Wait));
            lock (Log) Assert.True(finished == signal.Task, "Timed out. Events: " + string.Join(" | ", Log));
        }

        public Task UntilLogged(string prefix) => Until(log => log.Any(e => e.StartsWith(prefix)));

        public void Dispose() => Watcher.Dispose();
    }

    static void Append(string path, int bytes)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(new byte[bytes]);
    }

    [Theory]
    [InlineData("Unconfirmed 123456.crdownload", "")]
    [InlineData("Nicht bestätigt 987.crdownload", "")]
    [InlineData("photo.jpg.crdownload", "photo.jpg")]
    [InlineData("report 2024.pdf.crdownload", "report 2024.pdf")]
    [InlineData("song.mp3.part", "song.mp3")]
    [InlineData("setup.exe.opdownload", "setup.exe")]
    public void Names_the_download(string temp, string name) => Assert.Equal(name, DownloadWatcher.NameOf(temp));

    [Fact]
    public async Task Follows_a_Chrome_download_to_the_end()
    {
        using var folder = new TempFolder();
        using var recorder = new Recorder(folder.Path);

        string placeholder = Path.Combine(folder.Path, "Unconfirmed 123456.crdownload");
        File.WriteAllBytes(placeholder, new byte[1000]);
        await recorder.UntilLogged("started ");
        Assert.Single(recorder.Watcher.Active);

        string named = Path.Combine(folder.Path, "photo.jpg.crdownload");
        File.Move(placeholder, named);
        await recorder.UntilLogged("updated photo.jpg");

        Append(named, 50_000);
        await recorder.UntilLogged("updated photo.jpg 51000");
        Assert.Equal("photo.jpg", recorder.Watcher.Active.Single().Name);

        File.Move(named, Path.Combine(folder.Path, "photo.jpg"));
        await recorder.UntilLogged("finished ");

        Assert.Contains("finished photo.jpg photo.jpg 51000", recorder.Log);
        Assert.Empty(recorder.Watcher.Active);
        Assert.DoesNotContain(recorder.Log, e => e.StartsWith("cancelled"));
    }

    [Fact]
    public async Task Follows_a_Firefox_download_to_the_end()
    {
        using var folder = new TempFolder();
        using var recorder = new Recorder(folder.Path);

        // Firefox reserves the name with an empty file and writes into name.part next to it.
        string final = Path.Combine(folder.Path, "notes.pdf");
        File.WriteAllBytes(final, Array.Empty<byte>());
        string part = Path.Combine(folder.Path, "notes.pdf.part");
        File.WriteAllBytes(part, new byte[2048]);
        await recorder.UntilLogged("started notes.pdf");

        File.Delete(final);
        File.Move(part, final);
        await recorder.UntilLogged("finished ");

        Assert.Contains("finished notes.pdf notes.pdf 2048", recorder.Log);
        Assert.Equal(1, recorder.Log.Count(e => e.StartsWith("started")));
    }

    [Fact]
    public async Task A_cancelled_download_is_reported_as_such()
    {
        using var folder = new TempFolder();
        using var recorder = new Recorder(folder.Path);

        string temp = Path.Combine(folder.Path, "big.iso.crdownload");
        File.WriteAllBytes(temp, new byte[4096]);
        await recorder.UntilLogged("started big.iso");
        File.Delete(temp);

        await recorder.UntilLogged("cancelled big.iso");
        Assert.DoesNotContain(recorder.Log, e => e.StartsWith("finished"));
        Assert.Empty(recorder.Watcher.Active);
    }

    [Fact]
    public async Task A_browser_that_copies_instead_of_renaming_still_finishes()
    {
        using var folder = new TempFolder();
        using var recorder = new Recorder(folder.Path);

        string temp = Path.Combine(folder.Path, "data.csv.part");
        File.WriteAllBytes(temp, new byte[300]);
        await recorder.UntilLogged("started data.csv");
        File.WriteAllBytes(Path.Combine(folder.Path, "data.csv"), new byte[300]);
        File.Delete(temp);

        await recorder.UntilLogged("finished data.csv data.csv 300");
        Assert.DoesNotContain(recorder.Log, e => e.StartsWith("cancelled"));
    }

    [Fact]
    public async Task Leftovers_from_before_are_ignored_and_other_files_too()
    {
        using var folder = new TempFolder();
        string old = Path.Combine(folder.Path, "old.zip.crdownload");
        File.WriteAllBytes(old, new byte[10]);
        using var recorder = new Recorder(folder.Path);

        Append(old, 100);
        File.WriteAllBytes(Path.Combine(folder.Path, "notes.txt"), new byte[10]);
        File.Move(Path.Combine(folder.Path, "notes.txt"), Path.Combine(folder.Path, "notes2.txt"));
        await Task.Delay(800);

        Assert.Empty(recorder.Log);
        Assert.Empty(recorder.Watcher.Active);
    }

    [Fact]
    public async Task Measures_the_speed()
    {
        using var folder = new TempFolder();
        using var recorder = new Recorder(folder.Path);
        string temp = Path.Combine(folder.Path, "movie.mp4.crdownload");
        File.WriteAllBytes(temp, Array.Empty<byte>());
        await recorder.UntilLogged("started movie.mp4");

        for (int i = 0; i < 6; i++)
        {
            Append(temp, 200_000);
            await Task.Delay(120);
        }
        var download = recorder.Watcher.Active.Single();
        Assert.True(download.Bytes > 0);
        Assert.True(download.BytesPerSecond > 100_000, $"speed {download.BytesPerSecond:0}");

        // Stalled: the speed runs down to nothing.
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, download.BytesPerSecond);
        File.Delete(temp);
    }
}
