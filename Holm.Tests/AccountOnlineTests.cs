using WinNotch.Account;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>
/// The Holm account against the real Firebase project: two "PCs" share a throwaway account, which is deleted at the end.
/// Runs only in a Debug build with HOLM_DATA set to a test folder, so it can't touch your own Holm's settings:
/// <c>HOLM_DATA=&lt;temp folder&gt; dotnet test Holm.Tests -c Debug --filter FullyQualifiedName~AccountOnlineTests</c>
/// </summary>
[Trait("Category", "Online")]
public class AccountOnlineTests
{
    static Task Ui(Action action) { action(); return Task.CompletedTask; }

    [Fact]
    public async Task Two_pcs_share_settings_profile_achievements_friends_and_identity()
    {
        if (Diag.DevFolder == null || !AccountService.Available) return; // see the summary: never against real settings
        FriendsService.ProtectKey = (data, _) => data.ToArray();
        FriendsService.UnprotectKey = (data, _) => data.ToArray();
        string root = Path.Combine(Diag.Folder, "account-online-" + Guid.NewGuid().ToString("N")[..8]);
        string email = $"holm-selftest-{Guid.NewGuid():N}@example.com", password = "test password " + Guid.NewGuid().ToString("N")[..10];

        // PC 1: its own settings, a profile, a friend, some progress and a friends identity.
        var settings1 = new AppSettings { Beam = false, Animations = false, ClockStyle = "mono", ProfileName = "Test One", ProfileStatus = "testing" };
        var friend = FriendIdentity.Create("Lena", null).AsFriend();
        settings1.Friends.Add(friend);
        var book1 = new AchievementBook((string?)null);
        book1.Add(AchievementBook.Songs, 5);
        var friends1 = new FriendsService(settings1, Ui);
        friends1.EnsureMe();
        using var pc1 = new AccountService(settings1, book1, friends1, Ui, Path.Combine(root, "pc1"));

        // PC 2: defaults, nothing of its own.
        var settings2 = new AppSettings { Beam = false };
        var book2 = new AchievementBook((string?)null);
        var friends2 = new FriendsService(settings2, Ui);
        using var pc2 = new AccountService(settings2, book2, friends2, Ui, Path.Combine(root, "pc2"));

        try
        {
            await pc1.SignUpAsync(email, password, CancellationToken.None);
            Assert.True(pc1.SignedIn);
            Assert.Null(pc1.Problem);

            // A wrong password opens nothing.
            var wrong = await Assert.ThrowsAsync<AccountException>(() =>
                new AccountService(new AppSettings(), new AchievementBook((string?)null), new FriendsService(new AppSettings(), Ui), Ui, Path.Combine(root, "pc3"))
                    .SignInAsync(email, password + "x", CancellationToken.None));
            Assert.True(wrong.IsWrongLogin);

            // PC 2 signs in and gets everything.
            await pc2.SignInAsync(email, password, CancellationToken.None);
            Assert.Null(pc2.Problem);
            Assert.False(settings2.Animations);
            Assert.Equal("mono", settings2.ClockStyle);
            Assert.Equal("Test One", settings2.ProfileName);
            Assert.Equal("testing", settings2.ProfileStatus);
            Assert.Contains(settings2.Friends, f => f.Key == friend.Key);
            Assert.Equal(5, book2.Value(AchievementBook.Songs));
            Assert.Equal(settings1.FriendsTopic, settings2.FriendsTopic);
            Assert.Equal(friends1.Me!.PublicKey, friends2.Me!.PublicKey);

            // A change on PC 2 reaches PC 1; progress on PC 1 reaches PC 2.
            settings2.ClockStyle = "bold";
            book1.Add(AchievementBook.Songs, 10);
            await pc2.SyncAsync(CancellationToken.None);
            await pc1.SyncAsync(CancellationToken.None);
            await pc2.SyncAsync(CancellationToken.None);
            Assert.Null(pc1.Problem);
            Assert.Null(pc2.Problem);
            Assert.Equal("bold", settings1.ClockStyle);
            Assert.Equal(15, book2.Value(AchievementBook.Songs));

            // A friend removed on PC 1 goes on PC 2 too.
            settings1.Friends.Clear();
            await pc1.SyncAsync(CancellationToken.None);
            await pc2.SyncAsync(CancellationToken.None);
            Assert.DoesNotContain(settings2.Friends, f => f.Key == friend.Key);
        }
        finally
        {
            // The throwaway account goes, whatever happened above.
            if (pc1.SignedIn) await pc1.DeleteAsync(CancellationToken.None);
            else if (pc2.SignedIn) await pc2.DeleteAsync(CancellationToken.None);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }

        // Deleted: the account no longer opens. (Below.)
        await AssertGone(root, email, password);
    }

    sealed class NoInternet : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("No route to host");
    }

    [Fact]
    public async Task After_a_restart_it_stays_signed_in_and_without_internet_it_says_so()
    {
        if (Diag.DevFolder == null || !AccountService.Available) return;
        FriendsService.ProtectKey = (data, _) => data.ToArray();
        FriendsService.UnprotectKey = (data, _) => data.ToArray();
        string root = Path.Combine(Diag.Folder, "account-online-" + Guid.NewGuid().ToString("N")[..8]);
        string folder = Path.Combine(root, "pc1");
        string email = $"holm-selftest-{Guid.NewGuid():N}@example.com", password = "Bär-test " + Guid.NewGuid().ToString("N")[..10];
        var settings = new AppSettings { Beam = false, ClockStyle = "mono" };
        var book = new AchievementBook((string?)null);
        try
        {
            using (var before = new AccountService(settings, book, new FriendsService(settings, Ui), Ui, folder))
                await before.SignUpAsync(email, password, CancellationToken.None);

            // Holm starts again: signed in from account.json, a fresh token from the refresh token, and it syncs.
            using var after = new AccountService(settings, book, new FriendsService(settings, Ui), Ui, folder);
            Assert.True(after.SignedIn);
            Assert.Equal(email, after.Email);
            await after.SyncAsync(CancellationToken.None);
            Assert.Null(after.Problem);
            Assert.Equal(1, after.Rounds);

            // Without internet: a word for the person, still signed in, nothing lost.
            using var offline = new AccountService(settings, book, new FriendsService(settings, Ui), Ui, folder,
                                                   new FirebaseClient(FirebaseProject.ApiKey, FirebaseProject.ProjectId, new HttpClient(new NoInternet())));
            await offline.SyncAsync(CancellationToken.None);
            Assert.True(offline.SignedIn);
            Assert.Contains("internet", offline.Problem);

            // Signing in again with the umlaut password, as typed on another system (ä as a + ¨), works.
            using var other = new AccountService(new AppSettings { Beam = false }, new AchievementBook((string?)null),
                                                 new FriendsService(new AppSettings(), Ui), Ui, Path.Combine(root, "pc2"));
            await other.SignInAsync(email.ToUpperInvariant(), password.Normalize(System.Text.NormalizationForm.FormD), CancellationToken.None);
            Assert.True(other.SignedIn);

            await after.DeleteAsync(CancellationToken.None);
            Assert.False(after.SignedIn);
            Assert.False(File.Exists(Path.Combine(folder, "account.json")));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
        await AssertGone(root, email, password);
    }

    [Fact]
    public async Task Two_pcs_changing_and_syncing_at_once_end_up_the_same()
    {
        if (Diag.DevFolder == null || !AccountService.Available) return;
        FriendsService.ProtectKey = (data, _) => data.ToArray();
        FriendsService.UnprotectKey = (data, _) => data.ToArray();
        string root = Path.Combine(Diag.Folder, "account-online-" + Guid.NewGuid().ToString("N")[..8]);
        string email = $"holm-selftest-{Guid.NewGuid():N}@example.com", password = "test password " + Guid.NewGuid().ToString("N")[..10];
        var s1 = new AppSettings { Beam = false };
        var s2 = new AppSettings { Beam = false };
        var b1 = new AchievementBook((string?)null);
        var b2 = new AchievementBook((string?)null);
        using var pc1 = new AccountService(s1, b1, new FriendsService(s1, Ui), Ui, Path.Combine(root, "pc1"));
        using var pc2 = new AccountService(s2, b2, new FriendsService(s2, Ui), Ui, Path.Combine(root, "pc2"));
        // The merge runs on "the UI thread": one lock stands in for it, as two PCs each have their own.
        try
        {
            await pc1.SignUpAsync(email, password, CancellationToken.None);
            await pc2.SignInAsync(email, password, CancellationToken.None);
            for (int round = 0; round < 20; round++)
            {
                lock (s1) { s1.QuickReactions = new() { "pc1-" + round }; b1.Add(AchievementBook.Songs); }
                lock (s2) { s2.QuietFrom = $"{round % 24:00}:00"; b2.Add(AchievementBook.Timers); }
                await Task.WhenAll(pc1.SyncAsync(CancellationToken.None), pc2.SyncAsync(CancellationToken.None));
                Assert.Null(pc1.Problem);
                Assert.Null(pc2.Problem);
            }
            await pc1.SyncAsync(CancellationToken.None);
            await pc2.SyncAsync(CancellationToken.None);
            Assert.Equal(new[] { "pc1-19" }, s2.QuickReactions);
            Assert.Equal("19:00", s1.QuietFrom);
            Assert.Equal(20, b1.Value(AchievementBook.Timers));
            Assert.Equal(20, b2.Value(AchievementBook.Songs));
        }
        finally
        {
            if (pc1.SignedIn) await pc1.DeleteAsync(CancellationToken.None);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
        await AssertGone(root, email, password);
    }

    static async Task AssertGone(string root, string email, string password)
    {
        var gone = await Assert.ThrowsAsync<AccountException>(() =>
            new AccountService(new AppSettings(), new AchievementBook((string?)null), new FriendsService(new AppSettings(), Ui), Ui, Path.Combine(root, "pc4"))
                .SignInAsync(email, password, CancellationToken.None));
        Assert.True(gone.IsWrongLogin);
    }

    [Fact]
    public async Task Only_changes_to_what_the_account_keeps_start_a_sync_and_a_friend_code_clash_is_asked_once()
    {
        if (Diag.DevFolder == null || !AccountService.Available) return;
        FriendsService.ProtectKey = (data, _) => data.ToArray();
        FriendsService.UnprotectKey = (data, _) => data.ToArray();
        string root = Path.Combine(Diag.Folder, "account-online-" + Guid.NewGuid().ToString("N")[..8]);
        string email = $"holm-selftest-{Guid.NewGuid():N}@example.com", password = "test password " + Guid.NewGuid().ToString("N")[..10];

        var settings1 = new AppSettings { Beam = false };
        var friends1 = new FriendsService(settings1, Ui);
        friends1.EnsureMe();
        using var pc1 = new AccountService(settings1, new AchievementBook((string?)null), friends1, Ui, Path.Combine(root, "pc1"));

        // PC 2 has its own friends identity and a friend of its own.
        var settings2 = new AppSettings { Beam = false };
        var friends2 = new FriendsService(settings2, Ui);
        friends2.EnsureMe();
        var own = FriendIdentity.Create("Max", null).AsFriend();
        settings2.Friends.Add(own);
        using var pc2 = new AccountService(settings2, new AchievementBook((string?)null), friends2, Ui, Path.Combine(root, "pc2"));
        int asked = 0;
        pc2.IdentityQuestion += () => asked++;

        try
        {
            await pc1.SignUpAsync(email, password, CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(8)); // the pull Start() makes 5 seconds after signing in
            int rounds = pc1.Rounds;

            // Saving for something the account doesn't keep (the inbox's catch-up point) starts no sync...
            settings1.FriendsSince = 12345;
            settings1.Save();
            await Task.Delay(TimeSpan.FromSeconds(11));
            Assert.Equal(rounds, pc1.Rounds);
            // ...a setting the account keeps does, a few seconds later.
            settings1.ClockStyle = "mono";
            settings1.Save();
            await Task.Delay(TimeSpan.FromSeconds(12));
            Assert.Equal(rounds + 1, pc1.Rounds);

            // PC 2 joins: two friend codes, and PC 2 has friends under its own. It's asked once, not at every sync.
            await pc2.SignInAsync(email, password, CancellationToken.None);
            await pc2.SyncAsync(CancellationToken.None);
            await pc2.SyncAsync(CancellationToken.None);
            Assert.Equal(1, asked);
            Assert.NotEqual(settings1.FriendsTopic, settings2.FriendsTopic);
            Assert.Equal("mono", settings2.ClockStyle); // the rest syncs meanwhile

            // The account's code wins: PC 2 takes it and keeps its own friend (who's told the new code).
            await pc2.ChooseIdentityAsync(true);
            Assert.Equal(settings1.FriendsTopic, settings2.FriendsTopic);
            Assert.Equal(friends1.Me!.PublicKey, friends2.Me!.PublicKey);
            Assert.Contains(settings2.Friends, f => f.Key == own.Key);
            await pc1.SyncAsync(CancellationToken.None);
            Assert.Contains(settings1.Friends, f => f.Key == own.Key);
        }
        finally
        {
            if (pc1.SignedIn) await pc1.DeleteAsync(CancellationToken.None);
            else if (pc2.SignedIn) await pc2.DeleteAsync(CancellationToken.None);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
        await AssertGone(root, email, password);
    }
}
