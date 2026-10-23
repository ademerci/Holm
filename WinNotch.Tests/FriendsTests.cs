using System.Security.Cryptography;
using System.Text;
using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class FriendCodeTests
{
    [Fact]
    public void A_friend_code_survives_being_pasted_into_a_chat()
    {
        using var lena = FriendIdentity.Create("Lena Hoffmann", null);
        var card = FriendIdentity.ParseFriendCode($"hey, add me 👉 Add me as a friend in WinNotch: {lena.FriendCode} (see you!)");

        Assert.NotNull(card);
        Assert.Equal("Lena Hoffmann", card!.Name);
        Assert.Equal(lena.Topic, card.Topic);
        Assert.Equal(lena.PublicKey, card.Key);
        Assert.Null(card.Server); // the default server isn't spelled out
    }

    [Fact]
    public void A_code_carries_its_own_server_when_it_isnt_the_default()
    {
        using var noah = FriendIdentity.Create("Noah", "https://ntfy.example.com/");
        Assert.Equal("https://ntfy.example.com", FriendIdentity.ParseFriendCode(noah.FriendCode)!.Server);
    }

    [Fact]
    public void Damaged_or_missing_codes_are_no_friends()
    {
        using var lena = FriendIdentity.Create("Lena", null);
        Assert.Null(FriendIdentity.ParseFriendCode(lena.FriendCode[..^8] + "AAAAAAAA"));
        Assert.Null(FriendIdentity.ParseFriendCode("WNF1.not-a-real-code-but-long-enough-to-look-like-one-xxxxxxxx"));
        Assert.Null(FriendIdentity.ParseFriendCode("nothing to see here"));
        Assert.Null(FriendIdentity.ParseFriendCode(null));
    }

    [Theory]
    [InlineData("{\"t\":\"../../etc\",\"k\":\"KEY\"}")]                            // a topic that would leave the server's path
    [InlineData("{\"t\":\"wnf-ok\",\"k\":\"KEY\",\"s\":\"file:///C:/Windows\"}")]   // not a web server
    [InlineData("{\"t\":\"wnf-ok\",\"k\":\"bm90IGEga2V5\"}")]                       // not a key
    public void Codes_that_point_somewhere_odd_are_refused(string json)
    {
        using var someone = FriendIdentity.Create("x", null);
        json = json.Replace("KEY", someone.PublicKey);
        Assert.Null(FriendIdentity.ParseFriendCode("WNF1." + FriendIdentity.Base64Url(Encoding.UTF8.GetBytes(json))));
    }

    [Fact]
    public void Control_characters_are_taken_out_of_names()
    {
        using var someone = FriendIdentity.Create("x", null);
        string json = $"{{\"n\":\"Ev\\u202Ee\\r\\nAdmin\",\"t\":\"wnf-ok\",\"k\":\"{someone.PublicKey}\"}}";
        var card = FriendIdentity.ParseFriendCode("WNF1." + FriendIdentity.Base64Url(Encoding.UTF8.GetBytes(json)))!;
        Assert.DoesNotContain(card.Name, c => char.IsControl(c));
    }
}

public class FriendNoteTests
{
    static FriendNote Files(byte[] secret) => new(FriendNoteKind.Files, "Noah", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Secret: secret);

    [Fact]
    public void A_note_opens_for_the_friend_it_was_sealed_for()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        var secret = ShareCode.NewSecret();

        string text = FriendNotes.Seal(noah, lena.AsFriend(), Files(secret));
        var note = FriendNotes.Open(lena, text, out string sender);

        Assert.NotNull(note);
        Assert.Equal(FriendNoteKind.Files, note!.Kind);
        Assert.Equal(secret, note.Secret);
        Assert.Equal(noah.PublicKey, sender);
        Assert.True(text.Length < 1000, "notes are small"); // ntfy keeps messages up to 4 KB
    }

    [Fact]
    public void Nobody_else_can_open_it()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        using var eve = FriendIdentity.Create("Eve", null);
        string text = FriendNotes.Seal(noah, lena.AsFriend(), Files(ShareCode.NewSecret()));

        // Eve, even pointed at Lena's inbox, can't open a note meant for Lena.
        using var eveInLenasInbox = new FriendIdentity("Eve", lena.Topic, null, eve.Key);
        Assert.Null(FriendNotes.Open(eveInLenasInbox, text, out _));
    }

    [Fact]
    public void Nobody_can_pretend_to_be_a_friend()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        using var eve = FriendIdentity.Create("Eve", null);

        // Eve seals with her own key but puts Noah's public key in front.
        string forged = FriendNotes.Seal(eve, lena.AsFriend(), Files(ShareCode.NewSecret()));
        forged = FriendNotes.Prefix + noah.PublicKey + forged[(FriendNotes.Prefix.Length + eve.PublicKey.Length)..];

        Assert.Null(FriendNotes.Open(lena, forged, out _));
    }

    [Fact]
    public void A_note_cannot_be_moved_to_another_inbox()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = FriendNotes.Seal(noah, lena.AsFriend(), Files(ShareCode.NewSecret()));

        using var lenasOtherInbox = new FriendIdentity("Lena", "wnf-anotherinbox", null, lena.Key);
        Assert.Null(FriendNotes.Open(lenasOtherInbox, text, out _));
    }

    [Fact]
    public void Tampered_notes_are_rejected()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = FriendNotes.Seal(noah, lena.AsFriend(), Files(ShareCode.NewSecret()));
        // Change a character in the middle of the sealed part: the last one may only carry padding bits,
        // so changing it can leave the bytes exactly as they were.
        int start = text.LastIndexOf(' ') + 1, at = start + (text.Length - start) / 2;
        string tampered = text[..at] + (text[at] == 'A' ? 'B' : 'A') + text[(at + 1)..];

        Assert.Null(FriendNotes.Open(lena, tampered, out _));
        Assert.Null(FriendNotes.Open(lena, "wnf1 garbage", out _));
        Assert.Null(FriendNotes.Open(lena, "hello", out _));
    }

    [Fact]
    public void An_added_you_note_must_carry_the_senders_own_code()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        using var eve = FriendIdentity.Create("Eve", null);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var honest = FriendNotes.Open(lena, FriendNotes.Seal(noah, lena.AsFriend(), new FriendNote(FriendNoteKind.AddedYou, "Noah", now, Code: noah.FriendCode)), out _);
        Assert.Equal(noah.PublicKey, FriendIdentity.ParseFriendCode(honest!.Code)!.Key);

        // Eve can't get Lena to "add back" someone else: the code in the note has to be the sender's own.
        var sneaky = FriendNotes.Seal(eve, lena.AsFriend(), new FriendNote(FriendNoteKind.AddedYou, "Noah", now, Code: noah.FriendCode));
        Assert.Null(FriendNotes.Open(lena, sneaky, out _));
    }

    [Theory]
    [InlineData("{\"id\":\"a1\",\"time\":1700000000,\"event\":\"message\",\"topic\":\"t\",\"message\":\"wnf1 x y\"}", true, 1700000000L, "a1", "wnf1 x y")]
    [InlineData("{\"id\":\"k\",\"time\":1700000045,\"event\":\"keepalive\",\"topic\":\"t\"}", false, 1700000045L, "k", null)]
    public void Inbox_lines_are_read(string line, bool isMessage, long time, string id, string? text)
    {
        Assert.True(FriendInbox.TryParse(line, out bool m, out long t, out string? i, out string? x));
        Assert.Equal((isMessage, time, id, text), (m, t, i, x));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public void Junk_in_the_inbox_is_skipped(string line) => Assert.False(FriendInbox.TryParse(line, out _, out _, out _, out _));
}

public class LegacyFriendListTests
{
    [Fact]
    public void Duplicate_saved_cards_are_removed_and_the_latest_card_is_kept()
    {
        var friends = new List<Friend>
        {
            new() { Name = "Old name", Topic = "old-topic", Key = "same-key" },
            new() { Name = "Other friend", Topic = "other-topic", Key = "other-key" },
            new() { Name = "Current name", Topic = "current-topic", Key = "same-key" },
        };

        int removed = FriendsService.RemoveDuplicateFriendCards(friends);

        Assert.Equal(1, removed);
        Assert.Collection(friends,
            friend => Assert.Equal("other-key", friend.Key),
            friend =>
            {
                Assert.Equal("same-key", friend.Key);
                Assert.Equal("Current name", friend.Name);
                Assert.Equal("current-topic", friend.Topic);
            });
    }
}

/// <summary>
/// Friends meeting for real: notes through an ntfy server, files through the relay (needs a connection).
/// WINNOTCH_TEST_NTFY and WINNOTCH_TEST_RELAY ("host:port") point them at other servers.
/// </summary>
[Trait("Category", "Online")]
public class FriendsOnlineTests
{
    static string Ntfy => Environment.GetEnvironmentVariable("WINNOTCH_TEST_NTFY") ?? FriendIdentity.DefaultServer;

    static (string Host, int Port) Relay
    {
        get
        {
            string? relay = Environment.GetEnvironmentVariable("WINNOTCH_TEST_RELAY");
            int colon = relay?.LastIndexOf(':') ?? -1;
            return colon > 0 ? (relay![..colon], int.Parse(relay[(colon + 1)..])) : (RelayLink.DefaultHost, RelayLink.DefaultPort);
        }
    }

    /// <summary>Follows an inbox in the background and collects the notes that open.</summary>
    sealed class Inbox : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts = new();
        readonly Task _following;
        readonly List<(FriendNote Note, string Sender, long Age)> _notes = new();

        public Inbox(FriendIdentity me, long? since = null)
        {
            _following = FriendInbox.FollowAsync(me, since, (_, age, text) =>
            {
                if (FriendNotes.Open(me, text, out string sender) is { } note) lock (_notes) _notes.Add((note, sender, age));
                return Task.CompletedTask;
            }, _ => { }, null, _cts.Token);
        }

        public async Task<(FriendNote Note, string Sender, long Age)> NextAsync(FriendNoteKind kind)
        {
            for (int i = 0; i < 300; i++)
            {
                lock (_notes)
                {
                    int at = _notes.FindIndex(n => n.Note.Kind == kind);
                    if (at >= 0) { var found = _notes[at]; _notes.RemoveAt(at); return found; }
                }
                await Task.Delay(100);
            }
            throw new TimeoutException("No note arrived.");
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await _following;
        }
    }

    [Fact]
    public async Task Two_friends_add_each_other_and_send_a_file_bigger_than_any_note()
    {
        using var noah = FriendIdentity.Create("Noah", Ntfy);
        using var lena = FriendIdentity.Create("Lena", Ntfy);
        await using var lenasInbox = new Inbox(lena);
        await Task.Delay(1000); // the inbox is connected

        // Noah adds Lena from her code; her notch learns about it and can add him back.
        var lenaAsSeenByNoah = FriendIdentity.ParseFriendCode(lena.FriendCode)!;
        await FriendInbox.PostAsync(lenaAsSeenByNoah, FriendNotes.Seal(noah, lenaAsSeenByNoah,
            new FriendNote(FriendNoteKind.AddedYou, noah.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Code: noah.FriendCode)), Net.Timeout());
        var (added, addedBy, _) = await lenasInbox.NextAsync(FriendNoteKind.AddedYou);
        var noahAsSeenByLena = FriendIdentity.ParseFriendCode(added.Code)!;
        Assert.Equal(noah.PublicKey, addedBy);
        Assert.Equal(noah.PublicKey, noahAsSeenByLena.Key);

        // Noah drops a file on Lena: a fresh secret goes into her inbox, the file goes through the relay.
        using var source = new TempFolder();
        using var downloads = new TempFolder();
        var content = RandomNumberGenerator.GetBytes(3_000_000); // more than ntfy.sh takes as an attachment: the file never touches ntfy
        string path = source.File("Holiday.jpg", content);
        var secret = ShareCode.NewSecret();
        var ct = Net.Timeout(120);
        var (host, port) = Relay;

        await FriendInbox.PostAsync(lenaAsSeenByNoah, FriendNotes.Seal(noah, lenaAsSeenByNoah,
            new FriendNote(FriendNoteKind.Files, noah.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Secret: secret)), ct);
        var sending = Task.Run(async () =>
        {
            await using var stream = await RelayLink.ConnectAsync(host, port, ShareCode.RelayToken(secret), ct);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: true);
            return await FileTransfer.SendAsync(channel, "Noah", new[] { path }, null, ct);
        });

        var (invite, from, _) = await lenasInbox.NextAsync(FriendNoteKind.Files);
        Assert.Equal(noahAsSeenByLena.Key, from);
        await using (var stream = await RelayLink.ConnectAsync(host, port, ShareCode.RelayToken(invite.Secret!), ct))
        await using (var channel = new SecureChannel(stream, ShareCode.Key(invite.Secret!), initiator: false))
        {
            var saved = await FileTransfer.ReceiveAsync(channel, _ => Task.FromResult(true), downloads.Path, null, ct);
            Assert.Equal(content, File.ReadAllBytes(saved!.Single()));
        }
        Assert.True(await sending);
    }

    [Fact]
    public async Task A_note_left_while_the_friend_was_offline_is_picked_up_later()
    {
        using var noah = FriendIdentity.Create("Noah", Ntfy);
        using var lena = FriendIdentity.Create("Lena", Ntfy);
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5;
        var secret = ShareCode.NewSecret();

        await FriendInbox.PostAsync(lena.AsFriend(), FriendNotes.Seal(noah, lena.AsFriend(),
            new FriendNote(FriendNoteKind.Files, noah.Name, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Secret: secret)), Net.Timeout());
        await Task.Delay(2500);

        await using var lenasInbox = new Inbox(lena, since: before);
        var (note, _, age) = await lenasInbox.NextAsync(FriendNoteKind.Files);
        Assert.Equal(secret, note.Secret);
        Assert.InRange(age, 2, 60); // how long it waited, by the server's clock: stale invitations can be told apart
    }
}
