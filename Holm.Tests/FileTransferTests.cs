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
        if (OperatingSystem.IsWindows())
            Assert.Contains("ZoneId=3", File.ReadAllText(Path.Combine(target.Path, "photo.jpg") + ":Zone.Identifier"));
        else // no stray "photo.jpg:Zone.Identifier" file where streams don't exist
            Assert.Equal(new[] { "empty.txt", "photo.jpg" }, Directory.GetFiles(target.Path).Select(Path.GetFileName).Order());
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
        Assert.Equal("theirs", File.ReadAllText(saved!.Single()));
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

public class TextBeamTests
{
    // How Holm 1.6 read offers: the same record, without "Text".
    sealed record OfferBefore17(string Sender, List<FileBefore17> Files);
    sealed record FileBefore17(string Name, long Size);

    [Fact]
    public async Task A_beamed_text_arrives_marked_as_text_where_the_receiver_wants_it()
    {
        using var source = new TempFolder();
        using var downloads = new TempFolder();
        using var clipboard = new TempFolder();
        var (sender, receiver) = await Net.ChannelPairAsync();
        string path = source.File("Beamed text.txt", Encoding.UTF8.GetBytes("Meeting at 15:30 – Raum 2.14 ✓"));
        ShareOffer? seen = null;

        var receive = FileTransfer.ReceiveAsync(receiver, (offer, _) => { seen = offer; return Task.FromResult(true); },
                                                offer => offer.IsText ? clipboard.Path : downloads.Path, null, Net.Timeout());
        Assert.True(await FileTransfer.SendAsync(sender, "Lena", new[] { path }, null, Net.Timeout(), text: true));
        var saved = await receive;

        Assert.True(seen!.IsText);
        Assert.Equal(clipboard.Path, Path.GetDirectoryName(saved![0]));
        Assert.Equal("Meeting at 15:30 – Raum 2.14 ✓", File.ReadAllText(saved[0], Encoding.UTF8));
        Assert.Empty(Directory.GetFiles(downloads.Path));
    }

    [Fact]
    public async Task Ordinary_files_are_no_text()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        var (sender, receiver) = await Net.ChannelPairAsync();
        ShareOffer? seen = null;
        var receive = FileTransfer.ReceiveAsync(receiver, offer => { seen = offer; return Task.FromResult(true); }, target.Path, null, Net.Timeout());
        await FileTransfer.SendAsync(sender, "Lena", new[] { source.File("notes.txt", new byte[] { 65 }) }, null, Net.Timeout());
        await receive;
        Assert.False(seen!.IsText);
    }

    [Fact]
    public async Task Text_is_one_small_file()
    {
        using var source = new TempFolder();
        var (sender, _) = await Net.ChannelPairAsync();
        var two = new[] { source.File("a.txt", new byte[] { 1 }), source.File("b.txt", new byte[] { 2 }) };
        await Assert.ThrowsAsync<ArgumentException>(() => FileTransfer.SendAsync(sender, "Lena", two, null, Net.Timeout(), text: true));
        var big = new[] { source.File("big.txt", new byte[FileTransfer.MaxTextBytes + 1]) };
        await Assert.ThrowsAsync<ArgumentException>(() => FileTransfer.SendAsync(sender, "Lena", big, null, Net.Timeout(), text: true));
    }

    [Fact]
    public void Holm_before_17_reads_a_text_offer_as_an_ordinary_file()
    {
        string json = """{"Sender":"Lena","Files":[{"Name":"Beamed text.txt","Size":12}],"Text":true}""";
        var old = JsonSerializer.Deserialize<OfferBefore17>(json)!;
        Assert.Equal("Lena", old.Sender);
        Assert.Equal("Beamed text.txt", Assert.Single(old.Files).Name);
    }

    [Fact]
    public void Settings_from_before_text_beaming_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"SendFolders\":false}")!;
        Assert.False(old.SendFolders);
        Assert.True(old.BeamText);
    }
}

public class FileTransferAcceptLaterTests
{
    /// <summary>
    /// As in the notch: the person takes a moment to press Accept. Meanwhile the receiver listens for the sender's
    /// "cancelled" and stops listening on Accept; the files must still all arrive.
    /// </summary>
    [Theory]
    [InlineData(300)]
    [InlineData(1500)]
    public async Task Files_arrive_when_the_receiver_accepts_after_a_while(int thinkMs)
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        var big = RandomNumberGenerator.GetBytes(1_000_000);
        var (sender, receiver) = await Net.ChannelPairAsync();

        var receive = FileTransfer.ReceiveAsync(receiver, async (_, _) => { await Task.Delay(thinkMs); return true; }, target.Path, null, Net.Timeout(20));
        bool accepted = await FileTransfer.SendAsync(sender, "Lena", new[] { source.File("video.mp4", big) }, null, Net.Timeout(20));
        var saved = await receive;

        Assert.True(accepted);
        Assert.NotNull(saved);
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(target.Path, "video.mp4")));
    }
}

public class FileTransferUncancellableReadTests
{
    /// <summary>A connection whose reads can't be cancelled once started, as a socket read may not be on Windows.</summary>
    sealed class StubbornStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, CancellationToken.None);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => inner.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>
    /// The chat bug: the receiver pressed Accept and the transfer stood still. It waited for its "did the sender
    /// cancel?" read to stop before answering, and that read never stopped; the sender waited for the answer.
    /// </summary>
    [Fact]
    public async Task Accepting_works_even_when_a_started_read_cannot_be_cancelled()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        var big = RandomNumberGenerator.GetBytes(500_000);
        var (a, b) = await Net.LoopbackPairAsync();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var sender = new SecureChannel(new StubbornStream(a), key, initiator: true);
        await using var receiver = new SecureChannel(new StubbornStream(b), key, initiator: false);

        var receive = FileTransfer.ReceiveAsync(receiver, async (_, _) => { await Task.Delay(300); return true; }, target.Path, null, Net.Timeout(10));
        var send = FileTransfer.SendAsync(sender, "Lena", new[] { source.File("photo.jpg", big) }, null, Net.Timeout(10));

        Assert.True(await send);
        Assert.NotNull(await receive);
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(target.Path, "photo.jpg")));
    }

    [Fact]
    public async Task A_sender_who_cancels_while_the_receiver_decides_is_still_noticed()
    {
        using var source = new TempFolder();
        using var target = new TempFolder();
        var (a, b) = await Net.LoopbackPairAsync();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var sender = new SecureChannel(a, key, initiator: true); // the sender's own Cancel must work
        await using var receiver = new SecureChannel(new StubbornStream(b), key, initiator: false);
        using var stop = new CancellationTokenSource();

        var receive = FileTransfer.ReceiveAsync(receiver, async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return true; }, target.Path, null, Net.Timeout(10));
        var send = FileTransfer.SendAsync(sender, "Lena", new[] { source.File("a.txt", new byte[] { 1 }) }, null, stop.Token);
        await Task.Delay(300);
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        await Assert.ThrowsAsync<FileTransfer.SenderCancelledException>(() => receive);
        Assert.Empty(Directory.GetFiles(target.Path));
    }
}
