using System.Security.Cryptography;
using System.Text;
using WinNotch.Account;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class AccountVaultTests
{
    static readonly AccountKeys Keys = AccountKeys.Derive("Lena@Example.com ", "correct horse battery");

    [Fact]
    public void Firebase_gets_a_sign_in_secret_not_the_password()
    {
        Assert.DoesNotContain("correct horse", Keys.SignInSecret);
        Assert.Equal(43, Keys.SignInSecret.Length); // 32 bytes, base64url
        // The same email (in any case, with spaces) and password make the same secret on every computer.
        Assert.Equal(Keys.SignInSecret, AccountKeys.Derive("lena@example.com", "correct horse battery").SignInSecret);
        Assert.NotEqual(Keys.SignInSecret, AccountKeys.Derive("lena@example.com", "correct horse batterY").SignInSecret);
        Assert.NotEqual(Keys.SignInSecret, AccountKeys.Derive("other@example.com", "correct horse battery").SignInSecret);
    }

    [Fact]
    public void Data_key_opens_only_with_the_right_password_and_account()
    {
        byte[] dataKey = AccountVault.NewDataKey();
        string wrapped = AccountVault.WrapDataKey(Keys, "uid-1", dataKey);
        Assert.Equal(dataKey, AccountVault.UnwrapDataKey(Keys, "uid-1", wrapped));
        var wrong = AccountKeys.Derive("lena@example.com", "wrong password!!");
        Assert.ThrowsAny<CryptographicException>(() => AccountVault.UnwrapDataKey(wrong, "uid-1", wrapped));
        Assert.ThrowsAny<CryptographicException>(() => AccountVault.UnwrapDataKey(Keys, "uid-2", wrapped));
    }

    [Fact]
    public void Sections_are_sealed_for_their_account_and_name()
    {
        byte[] key = AccountVault.NewDataKey();
        byte[] plain = Encoding.UTF8.GetBytes("{\"Name\":\"Lena\"}");
        string sealedText = AccountVault.SealSection(key, "uid-1", "profile", plain);
        Assert.StartsWith("v1.", sealedText);
        Assert.DoesNotContain("Lena", sealedText);
        Assert.Equal(plain, AccountVault.OpenSection(key, "uid-1", "profile", sealedText));
        Assert.ThrowsAny<CryptographicException>(() => AccountVault.OpenSection(key, "uid-1", "settings", sealedText));
        Assert.ThrowsAny<CryptographicException>(() => AccountVault.OpenSection(key, "uid-2", "profile", sealedText));
        Assert.ThrowsAny<CryptographicException>(() => AccountVault.OpenSection(AccountVault.NewDataKey(), "uid-1", "profile", sealedText));
        // Every changed byte is noticed: in the nonce, the tag and the data. (Changing the last base64 character
        // could touch only its padding bits and leave the bytes as they were.)
        byte[] bytes = FriendIdentity.FromBase64Url(sealedText["v1.".Length..]);
        foreach (int at in new[] { 0, 12, 28, bytes.Length - 1 })
        {
            var changed = (byte[])bytes.Clone();
            changed[at] ^= 0x01;
            Assert.ThrowsAny<CryptographicException>(() => AccountVault.OpenSection(key, "uid-1", "profile", "v1." + FriendIdentity.Base64Url(changed)));
        }
    }
}

public class AccountSyncTests
{
    static Friend F(string key, string name = "Friend") => new() { Key = key, Topic = "t-" + key, Name = name };

    [Fact]
    public void A_new_pc_takes_the_accounts_settings_then_newer_changes_win()
    {
        // The first PC made the account with its settings.
        var first = new AppSettings { Animations = false, ClockStyle = "mono" };
        var firstState = new SyncState();
        SettingsSync.NoteLocalChanges(firstState, SettingsSync.Capture(first), 100);
        var (account, _) = SettingsSync.Merge(first, firstState, null);

        // A second PC with defaults joins: the account's choices win.
        var second = new AppSettings();
        var secondState = new SyncState();
        SettingsSync.NoteLocalChanges(secondState, SettingsSync.Capture(second), 200);
        var (merged, changed) = SettingsSync.Merge(second, secondState, account);
        Assert.False(second.Animations);
        Assert.Equal("mono", second.ClockStyle);
        Assert.Contains(nameof(AppSettings.Animations), changed);

        // Then the second PC changes one: it goes to the account, and on to the first PC.
        second.ClockStyle = "bold";
        SettingsSync.NoteLocalChanges(secondState, SettingsSync.Capture(second), 300);
        (merged, _) = SettingsSync.Merge(second, secondState, merged);
        Assert.Equal(300, merged.Stamps[nameof(AppSettings.ClockStyle)]);
        SettingsSync.NoteLocalChanges(firstState, SettingsSync.Capture(first), 400);
        SettingsSync.Merge(first, firstState, merged);
        Assert.Equal("bold", first.ClockStyle);
    }

    [Fact]
    public void Device_settings_and_private_things_never_go_to_the_account()
    {
        foreach (string name in new[]
                 {
                     nameof(AppSettings.Monitor), nameof(AppSettings.NotchPosition), nameof(AppSettings.CollapsedScale), nameof(AppSettings.AiPath),
                     nameof(AppSettings.Shelf), nameof(AppSettings.NoteText), nameof(AppSettings.ReminderList), nameof(AppSettings.WeatherPlace),
                     nameof(AppSettings.GifSearchKey), nameof(AppSettings.FriendsKey), nameof(AppSettings.Relay), nameof(AppSettings.SendQueue),
                 })
            Assert.DoesNotContain(name, SettingsSync.Names);
        // Every synced name is a real setting.
        Assert.Equal(SettingsSync.Names.Count, SettingsSync.Capture(new AppSettings()).Count);
    }

    [Fact]
    public void A_chosen_app_on_this_pc_is_not_replaced()
    {
        var mine = new AppSettings { Ai = "custom", AiPath = @"C:\Tools\ai.exe" };
        var state = new SyncState();
        SettingsSync.NoteLocalChanges(state, SettingsSync.Capture(mine), 10);
        var account = new SettingsPayload();
        foreach (var (k, v) in SettingsSync.Capture(new AppSettings { Ai = "gemini" })) { account.Values[k] = v; account.Stamps[k] = 50; }
        SettingsSync.Merge(mine, state, account);
        Assert.Equal("custom", mine.Ai);
    }

    [Fact]
    public void Achievements_keep_the_most_progress_and_the_first_unlock()
    {
        var mine = new AchievementsPayload { Counters = { ["songs"] = 40, ["friends"] = 2 }, Unlocked = { ["friend-1"] = 500 }, StreakDay = "2026-11-16" };
        var theirs = new AchievementsPayload { Counters = { ["songs"] = 70, ["timers"] = 3 }, Unlocked = { ["friend-1"] = 300, ["notes-1"] = 400 }, StreakDay = "2026-11-17" };
        var merged = AchievementsSync.Merge(mine, theirs);
        Assert.Equal(70, merged.Counters["songs"]);
        Assert.Equal(2, merged.Counters["friends"]);
        Assert.Equal(3, merged.Counters["timers"]);
        Assert.Equal(300, merged.Unlocked["friend-1"]);
        Assert.Equal(400, merged.Unlocked["notes-1"]);
        Assert.Equal("2026-11-17", merged.StreakDay);
    }

    [Fact]
    public void Friends_from_both_pcs_are_kept_and_a_removal_reaches_the_other()
    {
        // PC A has Lena and Max and syncs first.
        var stateA = new SyncState();
        var (listA, account, _) = FriendsSync.Merge(new List<Friend> { F("lena"), F("max") }, stateA, null, 100);
        Assert.Equal(2, account.Friends.Count);

        // PC B has Sam: after its sync it has all three, and so has the account.
        var stateB = new SyncState();
        var (listB, account2, changedB) = FriendsSync.Merge(new List<Friend> { F("sam") }, stateB, account, 200);
        Assert.True(changedB);
        Assert.Equal(new[] { "sam", "lena", "max" }, listB.Select(f => f.Key));

        // PC A removes Max, then syncs: Sam arrives, Max is gone from the account.
        listA.RemoveAll(f => f.Key == "max");
        var (listA2, account3, _) = FriendsSync.Merge(listA, stateA, account2, 300);
        Assert.Equal(new[] { "lena", "sam" }, listA2.Select(f => f.Key));
        Assert.True(account3.Removed.ContainsKey("max"));

        // PC B syncs again: Max goes there too.
        var (listB2, _, _) = FriendsSync.Merge(listB, stateB, account3, 400);
        Assert.DoesNotContain(listB2, f => f.Key == "max");

        // Max added again later on PC B counts again everywhere.
        listB2.Add(F("max"));
        var (_, account4, _) = FriendsSync.Merge(listB2, stateB, account3, 500);
        Assert.Contains(account4.Friends, f => f.Key == "max");
        Assert.False(account4.Removed.ContainsKey("max"));
        var (listA3, _, _) = FriendsSync.Merge(listA2, stateA, account4, 600);
        Assert.Contains(listA3, f => f.Key == "max");
    }

    [Fact]
    public void A_friends_picture_from_the_other_pc_fills_in()
    {
        var state = new SyncState();
        var remote = new FriendsPayload { Friends = { new SyncedFriend { Key = "lena", Topic = "t", Name = "Lena", Picture = "abc", Added = 5 } } };
        var (list, _, _) = FriendsSync.Merge(new List<Friend> { F("lena", "Lena") }, state, remote, 10);
        Assert.Equal("abc", Assert.Single(list).Picture);
    }

    [Fact]
    public void The_newer_profile_wins_and_a_new_pc_takes_the_accounts()
    {
        var mine = new AppSettings { ProfileName = "Noel" };
        var state = new SyncState();
        var (account, changed) = ProfileSync.Merge(mine, state, null, 100);
        Assert.False(changed);
        Assert.Equal("Noel", account.Name);

        var other = new AppSettings { ProfileName = "Somebody" };
        var otherState = new SyncState();
        (_, changed) = ProfileSync.Merge(other, otherState, account, 200);
        Assert.True(changed);
        Assert.Equal("Noel", other.ProfileName);

        other.ProfileStatus = "🎧 in focus";
        var (account2, _) = ProfileSync.Merge(other, otherState, account, 300);
        Assert.Equal(300, account2.Stamp);
        (_, changed) = ProfileSync.Merge(mine, state, account2, 400);
        Assert.True(changed);
        Assert.Equal("🎧 in focus", mine.ProfileStatus);
    }

    [Fact]
    public void The_friends_identity_is_asked_about_only_when_both_pcs_have_friends_of_their_own()
    {
        var state = new SyncState();
        Assert.Equal(IdentityStep.Nothing, IdentitySync.Decide(null, 0, null, state, null));
        Assert.Equal(IdentityStep.Upload, IdentitySync.Decide("a", 3, null, state, null));
        Assert.Equal(IdentityStep.Adopt, IdentitySync.Decide(null, 0, "b", state, null));
        Assert.Equal(IdentityStep.Nothing, IdentitySync.Decide("b", 3, "b", state, null));
        Assert.Equal(IdentityStep.Adopt, IdentitySync.Decide("a", 0, "b", state, null));
        Assert.Equal(IdentityStep.Ask, IdentitySync.Decide("a", 3, "b", state, null));
        Assert.Equal(IdentityStep.Adopt, IdentitySync.Decide("a", 3, "b", state, true));
        Assert.Equal(IdentityStep.Upload, IdentitySync.Decide("a", 3, "b", state, false));
        // This PC had the account's identity, and another PC replaced it since: follow without asking.
        state.IdentityFingerprint = "a";
        Assert.Equal(IdentityStep.Adopt, IdentitySync.Decide("a", 3, "b", state, null));
    }

    [Fact]
    public void Identity_fingerprint_names_inbox_and_public_key()
    {
        using var me = FriendIdentity.Create("Noel", null);
        var payload = new IdentityPayload { Topic = me.Topic, Key = Convert.ToBase64String(me.Key.ExportPkcs8PrivateKey()) };
        Assert.Equal(me.Topic + "|" + me.PublicKey, payload.Fingerprint());
    }
}
