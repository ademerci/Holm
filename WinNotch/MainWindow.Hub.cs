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
            new("chat", "", "Chat", "Messages with your friends, sealed so only you two can read them",
                () => { var page = new ChatPage(social, _hubFriend); _hubFriend = null; return page; },
                () => social.Chats.UnreadTotal),
        };
    }

    SocialContext Social() => new()
    {
        Friends = () => _friends.Friends,
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
        var friends = new List<Friend> { lena, jonas, mia };
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
            IsOnline = f => f.Key != "demo-jonas",
            Send = (_, _) => { },
            Retry = (_, _) => { },
        };
    }
#endif
}
