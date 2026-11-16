using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// The friends pages above the settings in the Holm window (the WPF app's MainWindow.Hub.cs), adding friends with
/// invite codes (MainWindow.Invites.cs) and chat (MainWindow.Chat.cs): messages go as sealed friend notes, the
/// history stays on this computer, and a message that comes in while you're not reading it pops out with Reply.
/// </summary>
public sealed partial class NotchWindow
{
    const string FriendGlyph = "", ChatsGlyph = "";

    /// <summary>The friend the next page should open with (the chat with them, say).</summary>
    Friend? _hubFriend;
    SocialContext? _social;
    ChatStore _chats = null!;
    string? _lastOfferedInvite;
    (Friend Friend, string Id)? _taskGif;
    Action? _stopTaskGif;

    void InitHub()
    {
        _chats = new ChatStore(Path.Combine(Diag.Folder, "chats.json"));
        _chats.Changed += _ => Dispatcher.UIThread.Post(() => _settingsWindow?.RefreshBadges());
        _friends.ChatReceived += OnChatReceived;
        _friends.GifReceived += OnGifReceived;
        _friends.ReactionReceived += OnReactionReceived;
        _friends.RemovedUs += friend => _chats.Forget(friend.Key);

        _friends.InviteAccepted += card =>
        {
            FriendsChanged();
            ShowPeek(FriendGlyph, $"{card.Name} is now your friend", "They used your invite code", null, seconds: 5);
        };
        _friends.LateAnswer += (card, yes) =>
        {
            FriendsChanged();
            if (yes) ShowPeek(FriendGlyph, $"You and {card.Name} are now friends", "They accepted your request", null, seconds: 5);
            else ShowPeek("", $"{card.Name} declined your request", "", RedBrush, seconds: 4);
        };
        _friends.InvitesChanged += () => _social?.Raise();
        _friends.FriendRequested += (_, _) => _social?.Raise();
        _friends.FriendRequestCancelled += (_, _) => _social?.Raise();
        _friends.FriendOnline += _ => _social?.Raise();
        _friends.ProfileReceived += _ => _social?.Raise();
        _friends.ActivityChanged += _ => _social?.Raise();
        _friends.ListChanged += () => _social?.Raise();
    }

    IReadOnlyList<HubPage> HubPages()
    {
        var social = Social();
        return new List<HubPage>
        {
            new("activity", "\uE716", "Active now", "What your friends are up to", () => new ActivityPage(social)),
            new("chat", ChatsGlyph, "Chat", "Messages with your friends, sealed so only you two can read them",
                () => { var page = new ChatPage(social, _hubFriend); _hubFriend = null; return page; },
                () => social.Chats.UnreadTotal),
            new("achievements", "\uE734", "Achievements", "What you've done with Holm",
                () => new AchievementsPage(_achievements)),
            new("add", FriendGlyph, "Add a friend", "Share your invite code, or type in a friend's",
                () => new AddFriendPage(social), () => social.Incoming().Count),
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
        Chats = _chats,
        Face = name => (FaceText(name), FaceBrush(name)),
        IsOnline = _friends.IsOnline,
        Send = SendChat,
        Retry = RetryChat,
        Invite = GetInviteAsync,
        AddByCode = AddFriendByCodeAsync,
        Incoming = () => _friendRequests,
        Pending = () => _friends.Pending,
        Answer = (card, id, yes) => _ = AnswerFromHubAsync(card, id, yes),
        Withdraw = p => _ = WithdrawAsync(p),
        LongCode = () => _settings.Beam ? _friends.EnsureMe().FriendCode : null,
        SendFiles = SendChatFiles,
        SendGif = SendGif,
        React = ReactTo,
        Wave = WaveAt,
        Stream = StreamTo,
        QuickReactions = () => ChatReaction.Quick(_settings.QuickReactions),
        SetQuickReactions = list => { _settings.QuickReactions = list.ToList(); _settings.Save(); },
        RecentGifs = () => _settings.RecentGifs,
        GifKey = () => GifSearch.Key(_settings),
        SetGifKey = key => { _settings.GifSearchKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim(); _settings.Save(); },
        Animate = () => _settings.Animations,
        Copy = TrySetClipboardAsync,
        Open = (page, friend) => { _hubFriend = friend; ShowHub(page); },
    };

    void SendChatFiles(Friend friend, string[] paths)
    {
        if (!_settings.Beam)
        {
            ShowPeek("", "Turn on Beam to send files", "", null);
            return;
        }
        if (_busy || _draggingOut)
        {
            ShowPeek("", "Wait until the current task finishes", "", null);
            return;
        }
        SendTo(new ShareTarget(null, friend), paths);
    }

    /// <summary>The friends list changed: the share targets and the Holm window follow.</summary>
    void FriendsChanged()
    {
        if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); }
        _social?.Raise();
    }

    // ───────────── invites ─────────────

    async Task<(OwnInvite? Invite, string? Error)> GetInviteAsync(bool fresh)
    {
        if (!_settings.Beam) return (null, "Turn on Beam in Settings to add friends");
        try { return (await _friends.InviteAsync(fresh, CancellationToken.None), null); }
        catch (IOException ex) { return (null, ex.Message); }
    }

    /// <summary>Adds the friend behind a typed or pasted code: an invite code, or an old long friend code.</summary>
    async Task<(bool Ok, string Message)> AddFriendByCodeAsync(string text)
    {
        if (!_settings.Beam) return (false, "Turn on Beam in Settings to add friends.");
        try
        {
            if (FriendInvite.Find(text) is { } code)
            {
                var (result, card) = await _friends.UseInviteAsync(code, TimeSpan.FromSeconds(25), CancellationToken.None);
                FriendsChanged();
                return result switch
                {
                    InviteResult.Added => (true, $"You and {card!.Name} are now friends."),
                    InviteResult.Sent => (true, $"Sent to {card!.Name}. You'll be friends as soon as their Holm is on."),
                    InviteResult.AlreadyFriends => (true, $"{card!.Name} is already your friend."),
                    InviteResult.OwnCode => (false, "That's your own code: send it to a friend."),
                    _ => (false, "That code doesn't work. It may have run out: ask your friend for a new one."),
                };
            }
            if (FriendIdentity.ParseFriendCode(text) is { } longCard)
            {
                if (_friends.IsMe(longCard)) return (false, "That's your own friend code.");
                if (_friends.Find(longCard.Key) != null) return (true, $"{longCard.Name} is already your friend.");
                var answer = await _friends.RequestAddAsync(longCard, TimeSpan.FromSeconds(25), CancellationToken.None);
                FriendsChanged();
                return answer switch
                {
                    FriendRequestResult.Accepted => (true, $"You and {longCard.Name} are now friends."),
                    FriendRequestResult.Declined => (false, $"{longCard.Name} declined."),
                    _ => (true, $"Request sent to {longCard.Name}. You'll be friends once they accept it in their notch."),
                };
            }
        }
        catch (IOException ex) { return (false, ex.Message + ". Check your connection and try again."); }
        return (false, "That isn't a Holm code. Codes look like HOLM-7KQ2X-9XF4M.");
    }

    async Task AnswerFromHubAsync(Friend card, string requestId, bool accept)
    {
        await AnswerFriendRequestAsync(card, requestId, accept);
        FriendsChanged();
    }

    async Task WithdrawAsync(PendingFriendRequest pending)
    {
        await _friends.CancelRequestAsync(pending);
        _social?.Raise();
    }

    /// <summary>An invite code was copied (from a chat, say): offer to add the friend behind it.</summary>
    bool OfferCopiedInvite(string? text)
    {
        if (FriendInvite.FindInMessage(text) is not { } code) return false;
        if (_friends.Invites.Any(i => i.Code == code) || code == _lastOfferedInvite) return true; // ours, or asked already
        _lastOfferedInvite = code;
        ShowTask(FriendGlyph, ShareBlue, "Holm invite code copied", "Add the friend who sent it?", null,
                 ("Add", () => _ = AddCopiedInviteAsync(code)), ("Not now", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(20);
        _taskHide.Start();
        return true;
    }

    async Task AddCopiedInviteAsync(string code)
    {
        var cts = BeginTask();
        ShowTask(FriendGlyph, ShareBlue, "Adding your friend…", "Looking up the invite", null, null, ("Cancel", CancelTask));
        var (ok, message) = await AddFriendByCodeAsync(code);
        if (cts.IsCancellationRequested)
        {
            EndTask("", RedBrush, "Cancelled", "");
            return;
        }
        if (ok) EndTask(FriendGlyph, ShareBlue, message.Split(". ")[0].TrimEnd('.'), message.Contains(". ") ? message[(message.IndexOf(". ") + 2)..] : "");
        else EndTask("", RedBrush, "Couldn't add the friend", message);
    }

    // ───────────── chat ─────────────

    /// <summary>Whether the Holm window is in front, showing the chat with this friend.</summary>
    bool Reading(Friend friend)
        => _settingsWindow is { IsActive: true, ShownView: ChatPage page } && page.Friend?.Key == friend.Key;

    void OnChatReceived(Friend friend, string id, string text, long sent)
        => Received(friend, new FriendMessage { Id = id, Mine = false, Text = text, Sent = sent, State = ChatState.Received });

    void OnGifReceived(Friend friend, string id, string url, long sent)
    {
        Received(friend, new FriendMessage { Id = id, Mine = false, Text = url, Gif = url, Sent = sent, State = ChatState.Received });
        RememberGif(url);
    }

    void Received(Friend friend, FriendMessage message)
    {
        bool reading = Reading(friend);
        if (!_chats.Add(friend.Key, message, unread: !reading)) return; // a note seen before
        Diag.Log(message.Gif != null ? "Chat GIF from a friend" : "Chat message from a friend");
        if (reading || !_settings.ChatPopups) return;
        if (_busy || _taskVisible || _sharing || _hiddenForFullscreen)
        {
            ShowPeek(null, $"Message from {friend.Name}", "", null, seconds: 4);
            return;
        }
        string preview = message.Gif != null ? "Sent a GIF" : message.Text.Replace('\n', ' ');
        if (preview.Length > 80) preview = preview[..80] + "…";
        // Like Instagram: react right from the notification, with the first two quick reactions.
        var quick = ChatReaction.Quick(_settings.QuickReactions);
        ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), friend.Name, preview, null,
                 ("Reply", () => { HideTask(); OpenChat(friend); }), (quick[1], () => InstantReact(friend, message, quick[1])), textIcon: true);
        ShowTaskExtra(quick[0], () => InstantReact(friend, message, quick[0]));
        if (message.Gif != null)
        {
            _taskGif = (friend, message.Id);
            _ = ShowTaskGifAsync(message.Gif, message.Id);
        }
        _taskHide.Interval = TimeSpan.FromSeconds(10);
        _taskHide.Start();
    }

    void StopTaskGif()
    {
        _stopTaskGif?.Invoke();
        _stopTaskGif = null;
        if (_taskGif != null) _taskThumb.Background = null;
    }

    async Task ShowTaskGifAsync(string url, string messageId)
    {
        try
        {
            var gif = await GifCache.LoadAsync(url, 96);
            if (gif == null || gif.Frames.Count == 0 || !_taskVisible || _taskGif?.Id != messageId) return;
            var brush = new Avalonia.Media.ImageBrush(gif.Frames[0]) { Stretch = Avalonia.Media.Stretch.UniformToFill };
            if (_settings.Animations && gif.Frames.Count > 1 && gif.Duration > TimeSpan.Zero)
                _stopTaskGif = GifPlayer.PlayBrush(brush, gif);
            _taskThumb.Background = brush;
            _taskThumb.IsVisible = true;
            _taskIconCircle.IsVisible = false;
        }
        catch (Exception ex) when (ex is not (StackOverflowException or AccessViolationException))
        {
            Diag.Log("GIF not shown in the notch: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>A reaction from the notification: sent, and the notch says so for a moment.</summary>
    void InstantReact(Friend friend, FriendMessage message, string emoji)
    {
        HideTask();
        if (_chats.Find(friend.Key, message.Id) is { } stored && stored.MyReaction != emoji) ReactTo(friend, stored, emoji);
        ShowPeek(null, $"Reacted {emoji}", "", null, seconds: 2);
    }

    void OnReactionReceived(Friend friend, string messageId, string? emoji)
    {
        if (!_chats.SetReaction(friend.Key, messageId, mine: false, emoji)) return;
        Diag.Log("Chat reaction from a friend");
        if (emoji == null || Reading(friend) || !_settings.ChatPopups) return;
        if (_chats.Find(friend.Key, messageId) is not { Mine: true }) return; // only reactions to what we wrote are news
        ShowPeek(null, $"{friend.Name} reacted {emoji}", "", null, seconds: 3);
    }

    /// <summary>Our reaction to a message: set at once on this computer, then sent to the friend (tried twice).</summary>
    void ReactTo(Friend friend, FriendMessage message, string? emoji)
    {
        string? clean = ChatReaction.Clean(emoji);
        if (!_chats.SetReaction(friend.Key, message.Id, mine: true, clean)) return;
        _ = DeliverReactionAsync(friend, message.Id, clean);
    }

    async Task DeliverReactionAsync(Friend friend, string messageId, string? emoji)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await _friends.SendReactionAsync(friend, messageId, emoji, cts.Token);
                return;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                Diag.Log("Chat reaction not sent: " + ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    /// <summary>Keeps the GIF among the recent ones (the link only).</summary>
    void RememberGif(string url)
    {
        _settings.RecentGifs.Remove(url);
        _settings.RecentGifs.Insert(0, url);
        if (_settings.RecentGifs.Count > 24) _settings.RecentGifs.RemoveRange(24, _settings.RecentGifs.Count - 24);
        _settings.Save();
    }

    /// <summary>Opens the Holm window on the chat with this friend.</summary>
    void OpenChat(Friend? friend)
    {
        _hubFriend = friend;
        ShowHub("chat");
    }

    /// <summary>Sends a text to a friend, as several messages if it's long. Each shows "Sending…" until it's in their inbox.</summary>
    void SendChat(Friend friend, string text)
    {
        if (ChatGif.FromTyped(text) is { } gif)
        {
            SendGif(friend, gif);
            return;
        }
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (string part in ChatText.Split(text))
        {
            var message = new FriendMessage { Id = Guid.NewGuid().ToString("N"), Mine = true, Text = part, Sent = now, State = ChatState.Sending };
            _chats.Add(friend.Key, message, unread: false);
            _ = DeliverAsync(friend, message);
            Achieve(AchievementBook.Messages);
        }
    }

    /// <summary>Sends a GIF by its link.</summary>
    void SendGif(Friend friend, string url)
    {
        if (ChatGif.Clean(url) is not { } clean) return;
        var message = new FriendMessage
        {
            Id = Guid.NewGuid().ToString("N"), Mine = true, Text = clean, Gif = clean,
            Sent = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), State = ChatState.Sending,
        };
        _chats.Add(friend.Key, message, unread: false);
        RememberGif(clean);
        _ = DeliverAsync(friend, message);
        Achieve(AchievementBook.Messages);
    }

    void RetryChat(Friend friend, FriendMessage message)
    {
        _chats.SetState(friend.Key, message.Id, ChatState.Sending);
        _ = DeliverAsync(friend, message);
    }

    async Task DeliverAsync(Friend friend, FriendMessage message)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _friends.SendChatAsync(friend, message.Id, message.Text, message.Sent, cts.Token, message.Gif);
            _chats.SetState(friend.Key, message.Id, ChatState.Sent);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            Diag.Log("Chat message not sent: " + ex.Message);
            _chats.SetState(friend.Key, message.Id, ChatState.Failed);
        }
    }
}
