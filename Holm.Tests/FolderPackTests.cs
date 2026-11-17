using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace WinNotch.Tests;

public sealed class FolderPackTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "HolmPackTests", Guid.NewGuid().ToString("N"));
    string Temp => Path.Combine(_root, "temp");

    public FolderPackTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    string MakeFolder(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, "sub", "deeper"));
        File.WriteAllText(Path.Combine(folder, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "world");
        File.WriteAllBytes(Path.Combine(folder, "sub", "deeper", "c.bin"), new byte[] { 1, 2, 3 });
        return folder;
    }

    [Fact]
    public void Packs_the_whole_folder_under_its_own_name()
    {
        string folder = MakeFolder("Holiday photos");
        var (zip, skipped) = FolderPack.Pack(folder, Temp, null, CancellationToken.None);
        Assert.Equal(0, skipped);
        Assert.Equal("Holiday photos.zip", Path.GetFileName(zip));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "Holiday photos/a.txt", "Holiday photos/sub/b.txt", "Holiday photos/sub/deeper/c.bin" },
                     archive.Entries.Select(e => e.FullName).OrderBy(n => n));
        using var reader = new StreamReader(archive.GetEntry("Holiday photos/sub/b.txt")!.Open());
        Assert.Equal("world", reader.ReadToEnd());
    }

    [Fact]
    public void Two_folders_of_the_same_name_get_two_zips()
    {
        string one = MakeFolder(Path.Combine("x", "Docs"));
        string two = MakeFolder(Path.Combine("y", "Docs"));
        var first = FolderPack.Pack(one, Temp, null, CancellationToken.None).Zip;
        var second = FolderPack.Pack(two, Temp, null, CancellationToken.None).Zip;
        Assert.Equal("Docs.zip", Path.GetFileName(first));
        Assert.Equal("Docs (2).zip", Path.GetFileName(second));
    }

    [Fact]
    public void An_empty_folder_stays_a_folder()
    {
        string folder = Path.Combine(_root, "Empty");
        Directory.CreateDirectory(folder);
        var (zip, _) = FolderPack.Pack(folder, Temp, null, CancellationToken.None);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal("Empty/", Assert.Single(archive.Entries).FullName);
    }

    [Fact]
    public void Cancelling_leaves_no_half_zip()
    {
        string folder = MakeFolder("Big");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FolderPack.Pack(folder, Temp, null, cancel.Token));
        Assert.Empty(Directory.GetFiles(Temp));
    }

    [Fact]
    public void Reports_progress_up_to_the_end()
    {
        string folder = MakeFolder("Progress");
        double last = -1;
        FolderPack.Pack(folder, Temp, new SyncProgress(p => last = p), CancellationToken.None);
        Assert.Equal(1, last, 3);
    }

    [Fact]
    public void Forgets_only_its_own_zips()
    {
        string folder = MakeFolder("Mine");
        var (zip, _) = FolderPack.Pack(folder, Temp, null, CancellationToken.None);
        string elsewhere = Path.Combine(_root, "keep.zip");
        File.WriteAllText(elsewhere, "not ours");
        FolderPack.Forget(new[] { zip, elsewhere }, Temp);
        Assert.False(File.Exists(zip));
        Assert.True(File.Exists(elsewhere));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Holiday photos", "Holiday photos.zip")]
    [InlineData(@"C:\Users\me\Holiday photos\", "Holiday photos.zip")]
    [InlineData(@"E:\", "E.zip")]
    public void Names_the_zip_after_the_folder(string folder, string expected)
    {
        if (!OperatingSystem.IsWindows()) return; // backslash paths
        Assert.Equal(expected, Path.GetFileName(FolderPack.ZipPathFor(folder, Temp)));
    }

    [Fact]
    public void Settings_from_before_folder_sending_still_load()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Waves\":false}")!;
        Assert.False(old.Waves);
        Assert.True(old.SendFolders);
    }

    sealed class SyncProgress : IProgress<double>
    {
        readonly Action<double> _report;
        public SyncProgress(Action<double> report) => _report = report;
        public void Report(double value) => _report(value);
    }
}
