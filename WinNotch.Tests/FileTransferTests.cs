using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class FileTransferTests
{
    [Fact]
    public async Task Accepted_files_arrive_complete_and_marked_as_downloaded()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        var big = RandomNumberGenerator.GetBytes(300_000); // several chunks
        var paths = new[] { source.File("photo.jpg", big), source.File("empty.txt", Array.Empty<byte>()) };
        var (sender, receiver) = await Net.ChannelPairAsync();
        ShareOffer? seen = null;
        double lastProgress = -1;

        var receive = FileTransfer.ReceiveAsync(receiver, offer => { seen = offer; return Task.FromResult(true); }, target.Path, null, Net.Timeout());
        bool accepted = await FileTransfer.SendAsync(sender, "Lena", paths, new SyncProgress(p => lastProgress = p), Net.Timeout());
        var saved = await receive;

        Assert.True(accepted);
        Assert.Equal(1, lastProgress);
        Assert.NotNull(seen);
        Assert.Equal("Lena", seen!.Sender);
        Assert.Equal(new[] { "photo.jpg", "empty.txt" }, seen.Files.Select(f => f.Name));
        Assert.Equal(300_000, seen.TotalBytes);
        Assert.NotNull(saved);
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(target.Path, "photo.jpg")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(target.Path, "empty.txt")));
        Assert.Contains("ZoneId=3", File.ReadAllText(Path.Combine(target.Path, "photo.jpg") + ":Zone.Identifier"));
    }

    [Fact]
    public async Task Declined_offers_write_nothing()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        var (sender, receiver) = await Net.ChannelPairAsync();

        var receive = FileTransfer.ReceiveAsync(receiver, _ => Task.FromResult(false), target.Path, null, Net.Timeout());
        bool accepted = await FileTransfer.SendAsync(sender, "Lena", new[] { source.File("a.txt", new byte[] { 1, 2, 3 }) }, null, Net.Timeout());

        Assert.False(accepted);
        Assert.Null(await receive);
        Assert.Empty(Directory.GetFiles(target.Path));
    }

    [Fact]
    public async Task Existing_files_are_not_overwritten()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        target.File("notes.txt", Encoding.UTF8.GetBytes("mine"));
        var (sender, receiver) = await Net.ChannelPairAsync();

        var receive = FileTransfer.ReceiveAsync(receiver, _ => Task.FromResult(true), target.Path, null, Net.Timeout());
        await FileTransfer.SendAsync(sender, "Lena", new[] { source.File("notes.txt", Encoding.UTF8.GetBytes("theirs")) }, null, Net.Timeout());
        var saved = await receive;

        Assert.Equal("mine", File.ReadAllText(Path.Combine(target.Path, "notes.txt")));
        Assert.Equal(Path.Combine(target.Path, "notes (1).txt"), saved!.Single());
        Assert.Equal("theirs", File.ReadAllText(saved.Single()));
    }

    // ───────────── a sender that doesn't play by the rules ─────────────

    const byte Offer = 1, Answer = 2, Data = 3, Done = 4;

    static byte[] OfferFrame(string sender, params (string Name, long Size)[] files)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { Sender = sender, Files = files.Select(f => new { f.Name, f.Size }).ToList() });
        return new[] { Offer }.Concat(json).ToArray();
    }

    [Fact]
    public async Task A_path_in_the_file_name_cannot_escape_the_target_folder()
    {
        using var target = new TempFolder();
        var (evil, receiver) = await Net.ChannelPairAsync();
        var ct = Net.Timeout();

        var receive = FileTransfer.ReceiveAsync(receiver, _ => Task.FromResult(true), target.Path, null, ct);
        await evil.SendAsync(OfferFrame("Mallory", (@"..\..\Windows\evil.exe", 3)), ct);
        Assert.Equal(new byte[] { Answer, 1 }, await evil.ReceiveAsync(ct));
        await evil.SendAsync(new byte[] { Data, 1, 2, 3 }, ct);
        await evil.SendAsync(new[] { Done }, ct);
        await evil.ReceiveAsync(ct); // the receiver's confirmation
        var saved = await receive;

        Assert.Equal(Path.Combine(target.Path, "evil.exe"), saved!.Single());
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(target.Path, @"..\..\Windows\evil.exe"))));
    }

    [Fact]
    public async Task More_data_than_offered_is_refused_and_the_partial_file_removed()
    {
        using var target = new TempFolder();
        var (evil, receiver) = await Net.ChannelPairAsync();
        var ct = Net.Timeout();

        var receive = FileTransfer.ReceiveAsync(receiver, _ => Task.FromResult(true), target.Path, null, ct);
        await evil.SendAsync(OfferFrame("Mallory", ("small.txt", 4)), ct);
        await evil.ReceiveAsync(ct);
        await evil.SendAsync(new byte[] { Data, 1, 2, 3, 4, 5, 6, 7, 8 }, ct);

        await Assert.ThrowsAsync<InvalidDataException>(() => receive);
        Assert.Empty(Directory.GetFiles(target.Path));
    }

    [Fact]
    public async Task A_dropped_connection_mid_file_leaves_no_partial_file()
    {
        using var target = new TempFolder();
        var (evil, receiver) = await Net.ChannelPairAsync();
        var ct = Net.Timeout();

        var receive = FileTransfer.ReceiveAsync(receiver, _ => Task.FromResult(true), target.Path, null, ct);
        await evil.SendAsync(OfferFrame("Mallory", ("big.bin", 1000)), ct);
        await evil.ReceiveAsync(ct);
        await evil.SendAsync(new byte[] { Data, 1, 2, 3 }, ct);
        await evil.DisposeAsync(); // hang up

        await Assert.ThrowsAnyAsync<Exception>(() => receive);
        Assert.Empty(Directory.GetFiles(target.Path));
    }

    [Theory]
    [InlineData(@"..\..\evil.txt", "evil.txt")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts", "hosts")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("x:y?.txt", "x_y_.txt")]
    [InlineData("...", "file")]
    [InlineData("  spaced.txt  ", "spaced.txt")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("trailing.dot.", "trailing.dot")]
    [InlineData("holiday\u202Egpj.exe", "holiday_gpj.exe")] // would show as "holidayexe.jpg"
    public void SafeFileName_keeps_only_a_plain_name(string? name, string expected)
    {
        Assert.Equal(expected, FileTransfer.SafeFileName(name));
    }

    [Fact]
    public void SafeFileName_shortens_long_names_but_keeps_the_extension()
    {
        string safe = FileTransfer.SafeFileName(new string('a', 400) + ".pdf");
        Assert.Equal(150, safe.Length);
        Assert.EndsWith(".pdf", safe);
    }

    /// <summary>Progress&lt;T&gt; posts to a thread pool; this reports right away so the test can check the last value.</summary>
    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
