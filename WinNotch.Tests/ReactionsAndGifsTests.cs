using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class ReactionTests
{
    static string Id() => Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData("❤️")]
    [InlineData("👍")]
    [InlineData("👍🏽")]   // with a skin tone
    [InlineData("🫡")]
    [InlineData("‼️")]
    public void An_emoji_is_a_reaction(string emoji) => Assert.Equal(emoji, ChatReaction.Clean(emoji));

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData("❤️❤️")]
    [InlineData("hi")]
    [InlineData("👍 ")]   // trimmed, so this one is fine…
    [InlineData("\u0007")]
    public void Anything_else_is_not(string text)
    {
        if (text == "👍 ") Assert.Equal("👍", ChatReaction.Clean(text));
        else Assert.Null(ChatReaction.Clean(text));
    }

    [Fact]
    public void Quick_reactions_are_instagrams_until_changed_and_always_six_without_repeats()
    {
        Assert.Equal(ChatReaction.Defaults, ChatReaction.Quick(null));
        var mine = ChatReaction.Quick(new[] { "🔥", "🔥", "nope", "😂" });
        Assert.Equal(6, mine.Count);
        Assert.Equal("🔥", mine[0]);
        Assert.Equal("😂", mine[1]);
        Assert.Equal(mine.Count, mine.Distinct().Count());
        Assert.Equal(ChatReaction.QuickCount, ChatReaction.Quick(ChatReaction.Picker).Count);
    }

    [Fact]
    public void A_reaction_travels_sealed_and_can_be_taken_away()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string target = Id();

        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Reaction, "Noah", 5, Text: "😂", MessageId: target)), out string from);
        Assert.Equal(FriendNoteKind.Reaction, note!.Kind);
        Assert.Equal("😂", note.Text);
        Assert.Equal(target, note.MessageId);
        Assert.Equal(me.PublicKey, from);

        var removed = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Reaction, "Noah", 6, MessageId: target)), out _);
        Assert.Equal(FriendNoteKind.Reaction, removed!.Kind);
        Assert.Null(removed.Text);
    }

    [Fact]
    public void A_reaction_with_text_or_without_a_message_id_is_left_unopened()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        Assert.Null(FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Reaction, "Noah", 5, Text: "lol", MessageId: Id())), out _));
        Assert.Null(FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Reaction, "Noah", 5, Text: "❤️", MessageId: "x")), out _));
    }

    [Fact]
    public void Each_side_has_one_reaction_per_message_and_it_is_kept()
    {
        string path = Path.Combine(Path.GetTempPath(), "holm-react-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ChatStore(path);
            int changes = 0;
            store.Changed += _ => changes++;
            store.Add("k", new FriendMessage { Id = "1", Text = "hey", Sent = 1, State = ChatState.Received }, unread: false);

            Assert.True(store.SetReaction("k", "1", mine: true, "❤️"));
            Assert.True(store.SetReaction("k", "1", mine: false, "😂"));
            Assert.False(store.SetReaction("k", "1", mine: true, "❤️")); // the same again changes nothing
            Assert.True(store.SetReaction("k", "1", mine: true, "👍"));   // another one replaces it
            Assert.False(store.SetReaction("k", "nope", mine: true, "👍"));
            Assert.Equal(4, changes);

            var again = new ChatStore(path).With("k")[0];
            Assert.Equal("👍", again.MyReaction);
            Assert.Equal("😂", again.TheirReaction);

            Assert.True(store.SetReaction("k", "1", mine: false, null));
            Assert.Null(store.With("k")[0].TheirReaction);
        }
        finally { File.Delete(path); }
    }
}

public class GifTests
{
    static string Id() => Guid.NewGuid().ToString("N");

    [Fact]
    public void A_gif_travels_as_its_link_so_older_holm_shows_the_link()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        const string url = "https://media.giphy.com/media/abc123/giphy.gif";

        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Chat, "Noah", 5, Text: url, MessageId: Id(), Gif: url)), out _);
        Assert.Equal(FriendNoteKind.Chat, note!.Kind);
        Assert.Equal(url, note.Gif);
        Assert.Equal(url, note.Text);

        // A plain message that happens to be a link stays a text.
        var text = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Chat, "Noah", 5, Text: url, MessageId: Id())), out _);
        Assert.Null(text!.Gif);
    }

    [Fact]
    public void A_gif_note_with_a_link_that_isnt_https_is_only_text()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Chat, "Noah", 5, Text: "http://example.com/a.gif", MessageId: Id(), Gif: "x")), out _);
        Assert.Null(note!.Gif);
        Assert.Equal("http://example.com/a.gif", note.Text);
    }

    [Theory]
    [InlineData("https://media.giphy.com/media/abc/giphy.gif", true)]
    [InlineData("  https://media.tenor.com/x/y.gif ", true)]
    [InlineData("http://media.giphy.com/media/abc/giphy.gif", false)]
    [InlineData("https://localhost/a.gif", false)]
    [InlineData("https://router/a.gif", false)]
    [InlineData("https://user:pw@example.com/a.gif", false)]
    [InlineData("https://192.168.1.1/a.gif", false)]
    [InlineData("file:///C:/a.gif", false)]
    [InlineData("not a link", false)]
    public void Only_public_https_links_are_gifs(string url, bool ok) => Assert.Equal(ok, ChatGif.Clean(url) != null);

    [Theory]
    [InlineData("https://example.com/funny.gif", "https://example.com/funny.gif")]
    [InlineData("https://giphy.com/gifs/cat-dance-JIX9t2j0ZTN9S", "https://media.giphy.com/media/JIX9t2j0ZTN9S/giphy.gif")]
    [InlineData("https://media.tenor.com/abc/tenor.gif", "https://media.tenor.com/abc/tenor.gif")]
    [InlineData("https://example.com/page", null)]
    [InlineData("look at this https://example.com/funny.gif", null)]
    public void A_typed_gif_link_is_sent_as_a_gif(string typed, string? gif) => Assert.Equal(gif, ChatGif.FromTyped(typed));

    [Fact]
    public void Giphy_results_give_a_small_preview_and_the_gif_to_send()
    {
        const string json = """
        { "data": [
          { "images": { "fixed_height": { "url": "https://media1.giphy.com/media/a/200.gif", "width": "300", "height": "200" },
                        "fixed_height_small": { "url": "https://media1.giphy.com/media/a/100.gif", "width": "150", "height": "100" } } },
          { "images": { "fixed_height": { "url": "http://insecure/x.gif" } } },
          { "nothing": 1 }
        ] }
        """;
        var results = GifSearch.Parse(json);
        var only = Assert.Single(results);
        Assert.Equal("https://media1.giphy.com/media/a/100.gif", only.Preview);
        Assert.Equal("https://media1.giphy.com/media/a/200.gif", only.Url);
        Assert.Equal(150, only.Width);
        Assert.Empty(GifSearch.Parse("{ nope"));
    }

    [Fact]
    public void Frame_timing_and_thinning_work_like_a_browser()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), GifDecoder.Delay(0)); // "as fast as possible" means a tenth
        Assert.Equal(TimeSpan.FromMilliseconds(100), GifDecoder.Delay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(40), GifDecoder.Delay(4));
        Assert.Equal(1, GifDecoder.Step(50, 120));
        Assert.Equal(2, GifDecoder.Step(200, 120));
        Assert.Equal(3, GifDecoder.Step(300, 120));
        Assert.True(GifDecoder.IsGif("GIF89a..."u8));
        Assert.False(GifDecoder.IsGif("\x89PNG\r\n"u8));
    }

    [Fact]
    public void Frames_are_drawn_over_each_other_and_clear_pixels_let_the_last_show()
    {
        const int w = 2, h = 1, stride = w * 4;
        var canvas = new byte[stride * h];
        GifDecoder.Draw(canvas, stride, w, h, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, 2, 1, 0, 0);
        GifDecoder.Draw(canvas, stride, w, h, new byte[] { 9, 9, 9, 0 }, 1, 1, 0, 0);       // clear: nothing changes
        GifDecoder.Draw(canvas, stride, w, h, new byte[] { 7, 7, 7, 255 }, 1, 1, 1, 0);     // at its place
        Assert.Equal(new byte[] { 1, 2, 3, 255, 7, 7, 7, 255 }, canvas);
        GifDecoder.Clear(canvas, stride, w, h, 0, 0, 1, 1);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 7, 7, 7, 255 }, canvas);
        GifDecoder.Draw(canvas, stride, w, h, new byte[] { 1, 1, 1, 255 }, 1, 1, 5, 5);     // outside: ignored
    }

    [Fact]
    public void A_gif_with_several_frames_comes_out_as_several_frozen_frames()
    {
        var encoder = new GifBitmapEncoder();
        foreach (var color in new[] { Colors.Red, Colors.Lime, Colors.Blue })
        {
            var pixels = Enumerable.Repeat(new[] { color.B, color.G, color.R, (byte)255 }, 16 * 8).SelectMany(p => p).ToArray();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4)));
        }
        using var stream = new MemoryStream();
        encoder.Save(stream);

        var gif = GifDecoder.Decode(stream.ToArray(), maxSide: 8);
        Assert.NotNull(gif);
        Assert.Equal(3, gif!.Frames.Count);
        Assert.All(gif.Frames, f => Assert.True(f.IsFrozen));
        Assert.Equal(8, gif.Width);  // made smaller: the longer side is 8
        Assert.Equal(4, gif.Height);
        Assert.Null(GifDecoder.Decode(new byte[] { 1, 2, 3 }));
    }
}

public class EqualizerTests
{
    [Fact]
    public void Bars_rise_quickly_on_a_beat_and_fall_slowly()
    {
        var levels = new LevelEnvelope();
        for (int i = 0; i < 20; i++) levels.Push(0.5f);   // the gain learns the loudness
        float loud = levels.Bar(3, 4);
        Assert.True(loud > 0.9f, $"{loud}");

        levels.Push(0);
        float fallen = levels.Bar(3, 4);
        Assert.True(fallen < loud && fallen > loud * 0.7f, $"one quiet chunk only lowers it a little: {fallen}");
        for (int i = 0; i < 40; i++) levels.Push(0);
        Assert.True(levels.Bar(3, 4) < 0.05f);
    }

    [Fact]
    public void Each_bar_shows_a_moment_a_little_earlier_than_the_next()
    {
        var levels = new LevelEnvelope();
        for (int i = 0; i < 30; i++) levels.Push(0.4f);
        for (int i = 0; i < 40; i++) levels.Push(0);
        levels.Push(0.4f); // a beat comes in: the last bar sees it first
        Assert.True(levels.Bar(3, 4) > 0.5f);
        Assert.True(levels.Bar(0, 4) < 0.05f);
    }

    [Fact]
    public void Quiet_music_moves_the_bars_as_much_as_loud_music()
    {
        var quiet = new LevelEnvelope();
        var loud = new LevelEnvelope();
        for (int i = 0; i < 200; i++)
        {
            float beat = i % 10 < 3 ? 1f : 0.3f;
            quiet.Push(0.05f * beat);
            loud.Push(0.8f * beat);
        }
        Assert.InRange(quiet.Bar(3, 4) - loud.Bar(3, 4), -0.1f, 0.1f);
    }

    [Fact]
    public void Bad_levels_are_silence()
    {
        var levels = new LevelEnvelope();
        levels.Push(float.NaN);
        levels.Push(-1);
        Assert.Equal(0, levels.Bar(3, 4));
    }
}
