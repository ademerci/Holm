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
#else
        var social = Social();
#endif
        return new List<HubPage>
        {
            new("activity", "", "Active now", "What your friends are up to",
                () => new ActivityPage(social)),
            new("chat", "", "Chat", "Messages with your friends, sealed so only you two can read them",
                () => { var page = new ChatPage(social, _hubFriend); _hubFriend = null; return page; },
                () => social.Chats.UnreadTotal),
        };
    }

    SocialContext Social() => _social ??= new()
    {
        Friends = () => _friends.Friends,
        ActivityOf = _friends.ActivityOf,
        Mine = MyActivity,
        Sharing = () => _settings.ShareActivity,
        Ping = () => _friends.PingAll(30),
        Wave = WaveAt,
        Chats = _chats,
        Face = name => (FaceText(name), FaceBrush(name)),
        IsOnline = _friends.IsOnline,
        Send = SendChat,
        Retry = RetryChat,
        Open = (page, friend) => { _hubFriend = friend; ShowHub(page); },
    };

#if DEBUG
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
            Send = (_, _) => { },
            Retry = (_, _) => { },
        };
    }
#endif
}
