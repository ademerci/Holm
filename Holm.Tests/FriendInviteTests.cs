using WinNotch.Sharing;

namespace WinNotch.Tests;

public class FriendInviteTests
{
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public void A_new_inbox_is_named_after_its_read_token_and_an_old_one_keeps_its_token()
    {
        Assert.Equal("wnf-zhz2d6o4y72a2pia7ohfmpfsrb", FriendIdentity.InboxFor(new string('o', 43))); // same as relay/src/notes.js
        using var fresh = FriendIdentity.Create("Lena", null);
        Assert.Equal(fresh.Topic, FriendIdentity.InboxFor(fresh.RelayReadToken));
        using var key = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var old = new FriendIdentity("Max", "wnf-" + new string('a', 26), null, key);
        Assert.NotEqual(old.Topic, FriendIdentity.InboxFor(old.RelayReadToken));
        Assert.Equal(43, old.RelayReadToken.Length);
    }

    [Fact]
    public void A_new_code_is_short_and_reads_back_as_itself()
    {
        Assert.Equal(16, FriendInvite.Length);
        Assert.Equal(600_000, FriendInvite.KdfIterations);
        for (int i = 0; i < 200; i++)
        {
            string code = FriendInvite.New();
            Assert.Matches("^HOLM-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", code);
            Assert.Equal(code, FriendInvite.Find(code));
        }
        Assert.NotEqual(FriendInvite.New(), FriendInvite.New());
    }

    [Theory]
    [InlineData("HOLM-7KQ2-X9XF-4M8P-2RTV")]
    [InlineData("holm-7kq2-x9xf-4m8p-2rtv")]
    [InlineData("  HOLM 7KQ2 X9XF 4M8P 2RTV ")]
    [InlineData("7KQ2-X9XF-4M8P-2RTV")]
    [InlineData("7kq2x9xf4m8p2rtv")]
    [InlineData("Add me as a friend on Holm: HOLM-7KQ2-X9XF-4M8P-2RTV (get Holm at github.com/ademerci/Holm)")]
    public void A_code_is_found_however_it_was_typed_or_pasted(string text) => Assert.Equal("HOLM-7KQ2-X9XF-4M8P-2RTV", FriendInvite.Find(text));

    [Fact]
    public void Letters_people_mix_up_are_read_as_what_they_look_like()
    {
        Assert.Equal("HOLM-11K2-X9XF-4M8P-2RTV", FriendInvite.Find("HOLM-ILK2-X9XF-4M8P-2RTV"));
        Assert.Equal("HOLM-00K2-X9XF-4M8P-2RTV", FriendInvite.Find("holm-oOk2-x9xf-4m8p-2rtv"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HOLM-7KQ2X")]
    [InlineData("HOLM-7KQ2X-9XF4M")] // retired 50-bit 10-char format
    [InlineData("HOLM-7KQ2-X9XF-4M8P-2RTVV")]
    [InlineData("HOLM-UUUU-X9XF-4M8P-2RTV")] // U isn't in the alphabet
    [InlineData("hi there")]
    public void Anything_else_is_no_code(string? text) => Assert.Null(FriendInvite.Find(text));

    [Fact]
    public void Copied_text_only_counts_with_the_holm_prefix()
    {
        Assert.Null(FriendInvite.FindInMessage("7KQ2X9XF4M8P2RTV"));
        Assert.Null(FriendInvite.FindInMessage("password12345678"));
        Assert.Equal("HOLM-7KQ2-X9XF-4M8P-2RTV", FriendInvite.FindInMessage("my code is HOLM-7KQ2-X9XF-4M8P-2RTV!"));
    }

    [Fact]
    public void Each_code_has_its_own_inbox_and_proof()
    {
        string a = FriendInvite.New(), b = FriendInvite.New();
        Assert.Equal(FriendInvite.Topic(a), FriendInvite.Topic(a));
        Assert.NotEqual(FriendInvite.Topic(a), FriendInvite.Topic(b));
        Assert.Matches("^holm-inv-[a-z2-7]{20}$", FriendInvite.Topic(a)); // a valid ntfy topic
        Assert.True(FriendInvite.IsProof(FriendInvite.Proof(a)));
        Assert.NotEqual(FriendInvite.Proof(a), FriendInvite.Proof(b));
        Assert.DoesNotContain(FriendInvite.Proof(a), FriendInvite.Topic(a));
    }

    [Fact]
    public void The_card_opens_with_its_code_only()
    {
        using var lena = FriendIdentity.Create("Lena", null);
        string code = FriendInvite.New();
        string card = FriendInvite.Seal(code, lena.FriendCode, Now + 3600);

        var friend = FriendInvite.Open(code, card, Now);
        Assert.NotNull(friend);
        Assert.Equal("Lena", friend!.Name);
        Assert.Equal(lena.PublicKey, friend.Key);
        Assert.Equal(lena.Topic, friend.Topic);

        Assert.Null(FriendInvite.Open(FriendInvite.New(), card, Now));           // another code
        Assert.Null(FriendInvite.Open(code, card, Now + 7200));                   // after it ran out
        Assert.Null(FriendInvite.Open(code, card[..^4] + "AAAA", Now));           // tampered
        Assert.Null(FriendInvite.Open(code, "hinv1 nonsense", Now));
        Assert.Null(FriendInvite.Open(code, null, Now));
    }

    [Fact]
    public void The_card_keeps_a_friends_own_server()
    {
        using var lena = FriendIdentity.Create("Lena", "https://ntfy.example.org");
        string code = FriendInvite.New();
        var friend = FriendInvite.Open(code, FriendInvite.Seal(code, lena.FriendCode, Now + 60), Now);
        Assert.Equal("https://ntfy.example.org", friend!.Server);
    }

    [Fact]
    public void A_request_made_with_a_code_carries_its_proof()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string code = FriendInvite.New();
        string requestId = Guid.NewGuid().ToString("N");
        var request = new FriendNote(FriendNoteKind.FriendRequest, "Noah", Now, Code: noah.FriendCode, RequestId: requestId,
                                     Invite: FriendInvite.Proof(code));

        var opened = FriendNotes.Open(lena, FriendNotes.Seal(noah, lena.AsFriend(), request), out string sender);
        Assert.NotNull(opened);
        Assert.Equal(noah.PublicKey, sender);
        Assert.Equal(FriendInvite.Proof(code), opened!.Invite);

        // Without a code, and with something that isn't a proof, there's none.
        var plain = FriendNotes.Open(lena, FriendNotes.Seal(noah, lena.AsFriend(), request with { Invite = null }), out _);
        Assert.Null(plain!.Invite);
        var junk = FriendNotes.Open(lena, FriendNotes.Seal(noah, lena.AsFriend(), request with { Invite = "not a proof" }), out _);
        Assert.Null(junk!.Invite);
    }
}

[Trait("Category", "Online")]
public class FriendInviteOnlineTests
{
    [Fact]
    public async Task A_friend_finds_the_card_behind_a_code_on_the_server()
    {
        using var lena = FriendIdentity.Create("Lena", null);
        string code = FriendInvite.New();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await FriendInbox.PublishInviteAsync(FriendInvite.Topic(code), FriendInvite.Seal(code, lena.FriendCode, now + 600), Net.Timeout());

        Friend? friend = null;
        for (int attempt = 0; attempt < 10 && friend == null; attempt++)
        {
            await Task.Delay(1000); // the server stores a new message a moment later
            var cards = await FriendInbox.FetchInviteAsync(FriendInvite.Topic(code), Net.Timeout());
            friend = cards.Select(c => FriendInvite.Open(code, c, now)).FirstOrDefault(f => f != null);
        }
        Assert.NotNull(friend);
        Assert.Equal(lena.PublicKey, friend!.Key);

        // Nothing waits behind a code nobody made.
        Assert.Empty(await FriendInbox.FetchInviteAsync(FriendInvite.Topic(FriendInvite.New()), Net.Timeout()));
    }
}
