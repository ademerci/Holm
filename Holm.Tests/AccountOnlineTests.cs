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

        // Deleted: the account no longer opens.
        var gone = await Assert.ThrowsAsync<AccountException>(() =>
            new AccountService(new AppSettings(), new AchievementBook((string?)null), new FriendsService(new AppSettings(), Ui), Ui, Path.Combine(root, "pc4"))
                .SignInAsync(email, password, CancellationToken.None));
        Assert.True(gone.IsWrongLogin);
    }
}
