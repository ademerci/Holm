using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>The pages above the settings in the Holm window: friends, chat and achievements.</summary>
public partial class MainWindow
{
    /// <summary>The friend the next page should open with (the chat with them, say).</summary>
    Friend? _hubFriend;
    /// <summary>The friends pages' view of the notch, made once; it tells open pages when friends change.</summary>
    SocialContext? _social;

    /// <param name="demo">For snapshots: made-up friends and messages only.</param>
    IReadOnlyList<HubPage> HubPages(bool demo)
    {
#if DEBUG
        var social = demo ? DemoSocial() : Social();
        var achievements = demo ? DemoAchievements() : _achievements;
#else
        var social = Social();
        var achievements = _achievements;
#endif
        return new List<HubPage>
        {
            new("activity", "", "Active now", "What your friends are up to",
                () => new ActivityPage(social)),
            new("chat", "", "Chat", "Messages with your friends, sealed so only you two can read them",
                () => { var page = new ChatPage(social, _hubFriend); _hubFriend = null; return page; },
                () => social.Chats.UnreadTotal),
            new("achievements", "", "Achievements", "What you've done with Holm",
                () => new AchievementsPage(achievements)),
        };
    }

    SocialContext Social() => _social ??= new()
    {
        Friends = () => _friends.Friends,
        ActivityOf = _friends.ActivityOf,
        Mine = MyActivity,
        Sharing = () => _settings.ShareActivity,
        Ping = () => _friends.PingAll(30),
        TrophiesOf = _friends.TrophiesOf,
        Wave = WaveAt,
        Chats = _chats,
        Face = name => (FaceText(name), FaceBrush(name)),
        IsOnline = _friends.IsOnline,
        Send = SendChat,
        Retry = RetryChat,
        Open = (page, friend) => { _hubFriend = friend; ShowHub(page); },
    };

#if DEBUG
    /// <summary>Made-up progress for the snapshots, in memory only.</summary>
    static AchievementBook DemoAchievements()
    {
        long day = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 86400;
        var unlocked = new Dictionary<string, long>();
        foreach (var (id, daysAgo) in new[] { ("friend-1", 20), ("beam-1", 19), ("chat-1", 3), ("wave-1", 3), ("profile", 2), ("timer-10", 1), ("streak-7", 0), ("color-10", 6) })
            unlocked[id] = day - daysAgo * 86400L;
        var counters = new Dictionary<string, long>
        {
            [AchievementBook.Friends] = 4, [AchievementBook.FilesSent] = 17, [AchievementBook.MegabytesSent] = 610, [AchievementBook.Messages] = 64,
            [AchievementBook.Waves] = 5, [AchievementBook.Timers] = 12, [AchievementBook.Screenshots] = 11, [AchievementBook.Colors] = 13,
            [AchievementBook.Songs] = 82, [AchievementBook.ProfilePicture] = 1, [AchievementBook.Streak] = 9, [AchievementBook.Reminders] = 4,
        };
        return new AchievementBook(counters, unlocked);
    }

    /// <summary>Made-up friends and a conversation for the snapshots; nothing of yours.</summary>
    SocialContext DemoSocial()
    {
        var lena = new Friend { Name = "Lena Hoffmann", Key = "demo-lena", Picture = DemoFace(), Status = "Back at 3" };
        var jonas = new Friend { Name = "Jonas", Key = "demo-jonas", Color = "#5E5CE6", Status = "🎧 in focus" };
        var mia = new Friend { Name = "Mia Keller", Key = "demo-mia", Picture = DemoFace(Colors.MediumPurple, Colors.HotPink) };
        var tim = new Friend { Name = "Tim", Key = "demo-tim", Color = "#FF9F0A", Status = "Exams until Friday" };
        var friends = new List<Friend> { lena, jonas, mia, tim };
        var chats = new ChatStore(null);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600;
        void Say(Friend who, bool mine, string text, long at) =>
            chats.Add(who.Key, new FriendMessage { Id = Guid.NewGuid().ToString("N"), Mine = mine, Text = text, Sent = at, State = mine ? ChatState.Sent : ChatState.Received }, unread: false);
        Say(lena, false, "Hey! Did the holiday photos arrive?", now);
        Say(lena, true, "Yes, all 48 MB in a few seconds 😄", now + 60);
        Say(lena, true, "The one at the lake is my favourite", now + 75);
        Say(lena, false, "Same! Want to stream the slideshow tonight?", now + 300);
        Say(lena, true, "Sure, 8 pm?", now + 360);
        chats.Add(lena.Key, new FriendMessage { Id = "demo-lena-1", Text = "Perfect 👍 I'll bring snacks", Sent = now + 400, State = ChatState.Received }, unread: true);
        Say(jonas, false, "Can you beam me the slides?", now - 7200);
        Say(mia, false, "Are you coming to the concert?", now - 600);
        return new SocialContext
        {
            Friends = () => friends,
            Chats = chats,
            Face = name => friends.FirstOrDefault(f => f.Name == name) is { } f && ProfilePicture.BrushOf(f.Picture) is { } picture
                ? ("", picture)
                : (Initials(name), friends.FirstOrDefault(f => f.Name == name)?.Color is { } c ? Frozen((Color)ColorConverter.ConvertFromString(c)) : AvatarColor(name)),
            IsOnline = f => f.Key != "demo-tim",
            ActivityOf = f => f.Key switch
            {
                "demo-lena" => new FriendActivity(FriendActivity.Music, "Blinding Lights", "The Weeknd", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 240),
                "demo-mia" => new FriendActivity(FriendActivity.Game, "Minecraft", null, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3000),
                _ => null,
            },
            Mine = () => new FriendActivity(FriendActivity.Timer, "On a timer", "24:10 left", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300),
            Sharing = () => true,
            TrophiesOf = f => f.Key switch { "demo-lena" => 14, "demo-mia" => 9, "demo-jonas" => 3, _ => null },
            Send = (_, _) => { },
            Retry = (_, _) => { },
        };
    }
#endif
}
