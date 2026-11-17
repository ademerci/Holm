using System.Text;
using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class ChatTests
{
    static string Id() => Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData('"')]   // JSON doubles quotes and backslashes
    [InlineData('\\')]
    [InlineData('x')]
    public void The_largest_message_still_fits_one_inbox_message(char fill)
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = new string(fill, ChatText.MaxBytes);
        string id = Id();

        string sealedNote = FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Chat, "Noah", 5, Text: text, MessageId: id));

        Assert.True(sealedNote.Length < 4096, $"{sealedNote.Length} characters");
        var note = FriendNotes.Open(lena, sealedNote, out _);
        Assert.Equal(FriendNoteKind.Chat, note!.Kind);
        Assert.Equal(text, note.Text);
        Assert.Equal(id, note.MessageId);
    }

    [Fact]
    public void Emoji_and_line_breaks_arrive_as_typed()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = string.Concat(Enumerable.Repeat("🎉", ChatText.MaxBytes / 4 - 3)) + "\nsee you"; // 988 + 8 bytes
        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Chat, "Noah", 5, Text: text, MessageId: Id())), out _);
        Assert.Equal(text, note!.Text);
    }

    [Fact]
    public void A_note_with_a_bad_id_or_too_much_text_is_left_unopened()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        Assert.Null(FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Chat, "Noah", 5, Text: "hi", MessageId: "nope")), out _));
        Assert.Null(FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Chat, "Noah", 5, Text: "  ", MessageId: Id())), out _));
    }

    [Fact]
    public void Control_characters_go_but_line_breaks_stay()
    {
        Assert.Equal("a\nb c", ChatText.Clean("a\r\nb\u0007 c"));
        Assert.Null(ChatText.Clean("\u0001\u0002"));
        Assert.Null(ChatText.Clean(new string('x', ChatText.MaxBytes + 1)));
    }

    [Fact]
    public void A_long_text_is_split_between_words_and_whole_characters()
    {
        string words = string.Join(' ', Enumerable.Repeat("holiday", 800)); // about 6400 bytes
        var parts = ChatText.Split(words);
        Assert.True(parts.Count >= 4);
        Assert.All(parts, p => Assert.True(Encoding.UTF8.GetByteCount(p) <= ChatText.MaxBytes));
        Assert.All(parts, p => Assert.DoesNotContain("holida ", p + " ")); // no word cut in half
        Assert.Equal(words.Replace(" ", ""), string.Concat(parts).Replace(" ", ""));

        string emoji = string.Concat(Enumerable.Repeat("👨‍👩‍👧", 200)); // one character of 18 bytes each
        var emojiParts = ChatText.Split(emoji);
        Assert.Equal(emoji, string.Concat(emojiParts));
        Assert.All(emojiParts, p => Assert.Equal(0, p.Length % "👨‍👩‍👧".Length));
    }

    [Fact]
    public void The_store_keeps_order_drops_repeats_and_counts_unread()
    {
        var store = new ChatStore(null);
        Assert.True(store.Add("k", new FriendMessage { Id = "b", Text = "second", Sent = 20 }, unread: true));
        Assert.True(store.Add("k", new FriendMessage { Id = "a", Text = "first", Sent = 10 }, unread: true)); // came late
        Assert.False(store.Add("k", new FriendMessage { Id = "a", Text = "first", Sent = 10 }, unread: true)); // the same note again
        Assert.Equal(new[] { "first", "second" }, store.With("k").Select(m => m.Text));
        Assert.Equal(2, store.Unread("k"));
        Assert.Equal(2, store.UnreadTotal);
        store.MarkRead("k");
        Assert.Equal(0, store.UnreadTotal);
        store.Forget("k");
        Assert.Empty(store.With("k"));
    }

    [Fact]
    public void The_store_keeps_the_last_messages_only()
    {
        var store = new ChatStore(null);
        for (int i = 0; i < ChatStore.Keep + 20; i++) store.Add("k", new FriendMessage { Id = i.ToString(), Text = i.ToString(), Sent = i }, unread: false);
        Assert.Equal(ChatStore.Keep, store.With("k").Count);
        Assert.Equal("20", store.With("k")[0].Text);
    }

    [Fact]
    public void The_history_survives_a_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), "holm-chat-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ChatStore(path);
            store.Add("k", new FriendMessage { Id = "1", Mine = true, Text = "hi 👋", Sent = 1, State = ChatState.Sending }, unread: false);
            store.SetState("k", "1", ChatState.Failed);
            store.Add("k", new FriendMessage { Id = "2", Text = "hey", Sent = 2, State = ChatState.Received }, unread: true);

            var again = new ChatStore(path);
            Assert.Equal(new[] { "hi 👋", "hey" }, again.With("k").Select(m => m.Text));
            Assert.Equal(ChatState.Failed, again.With("k")[0].State);
            Assert.Equal(1, again.Unread("k"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_history_file_starts_empty()
    {
        string path = Path.Combine(Path.GetTempPath(), "holm-chat-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new ChatStore(path).With("k"));
        }
        finally { File.Delete(path); }
    }
}
