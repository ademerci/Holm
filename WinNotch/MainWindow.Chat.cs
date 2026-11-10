using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Chat with friends: messages go as sealed friend notes, the history stays on this PC, and a message that comes in
/// while you're not reading it pops out of the notch with Reply.
/// </summary>
public partial class MainWindow
{
    ChatStore _chats = null!;

    void InitChat()
    {
        _chats = new ChatStore(Path.Combine(Diag.Folder, "chats.json"));
        _chats.Changed += _ => _settingsWindow?.RefreshBadges();
        _friends.ChatReceived += OnChatReceived;
        _friends.GifReceived += OnGifReceived;
        _friends.ReactionReceived += OnReactionReceived;
        _friends.RemovedUs += friend => _chats.Forget(friend.Key);
    }

    /// <summary>Whether the Holm window is in front, showing the chat with this friend.</summary>
    bool Reading(Friend friend)
        => _settingsWindow is { IsActive: true, ShownView: ChatPage page } && page.Friend?.Key == friend.Key;

    void OnChatReceived(Friend friend, string id, string text, long sent)
    {
        var message = new FriendMessage { Id = id, Mine = false, Text = text, Sent = sent, State = ChatState.Received };
        Received(friend, message);
    }

    void OnGifReceived(Friend friend, string id, string url, long sent)
    {
        var message = new FriendMessage { Id = id, Mine = false, Text = url, Gif = url, Sent = sent, State = ChatState.Received };
        Received(friend, message);
        RememberGif(url);
    }

    void Received(Friend friend, FriendMessage message)
    {
        bool reading = Reading(friend);
        if (!_chats.Add(friend.Key, message, unread: !reading)) return; // a note seen before
        Diag.Log(message.Gif != null ? "Chat GIF from a friend" : "Chat message from a friend");
        if (reading || !_settings.ChatPopups) return;
        if (_busy || _taskVisible || _sharing || _installingUpdate || _hiddenForFullscreen)
        {
            ShowPeek(null, $"Message from {friend.Name}", "", null, false, null, seconds: 4);
            return;
        }
        string preview = message.Gif != null ? "Sent a GIF" : message.Text.Replace('\n', ' ');
        if (preview.Length > 80) preview = preview[..80] + "…";
        // Like Instagram: react right from the notification, with the first two quick reactions.
        var quick = ChatReaction.Quick(_settings.QuickReactions);
        ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), friend.Name, preview, null,
                 ("Reply", () => { HideTask(); OpenChat(friend); }), (quick[1], () => InstantReact(friend, message, quick[1])), textIcon: true);
        ShowTaskExtra(quick[0], () => InstantReact(friend, message, quick[0]));
        // In color, as in the chat (the buttons would draw the emoji in black and white).
        TaskExtra.Content = ChatExtras.Emoji(quick[0], 15);
        TaskSecondary.Content = ChatExtras.Emoji(quick[1], 15);
        if (message.Gif != null)
        {
            _taskGif = (friend, message.Id);
            _ = ShowTaskGifAsync(message.Gif, message.Id);
        }
        _taskHide.Interval = TimeSpan.FromSeconds(10);
        _taskHide.Start();
    }

    /// <summary>A reaction from the notification: sent, and the notch says so for a moment.</summary>
    void InstantReact(Friend friend, FriendMessage message, string emoji)
    {
        HideTask();
        if (_chats.Find(friend.Key, message.Id) is { } stored && stored.MyReaction != emoji) ReactTo(friend, stored, emoji);
        ShowPeek(null, $"Reacted {emoji}", "", null, false, null, seconds: 2);
    }

    /// <summary>The GIF moves in the notification's picture, small, while the notification shows.</summary>
    async Task ShowTaskGifAsync(string url, string messageId)
    {
        var gif = await GifCache.LoadAsync(url, 96);
        if (gif == null || !_taskVisible || _taskGif?.Id != messageId) return; // the notification is gone or another one shows
        var brush = new System.Windows.Media.ImageBrush(gif.Frames[0]) { Stretch = System.Windows.Media.Stretch.UniformToFill };
        if (_settings.Animations && gif.Frames.Count > 1)
            brush.BeginAnimation(System.Windows.Media.ImageBrush.ImageSourceProperty, GifPlayer.Animation(gif));
        TaskThumb.Background = brush;
        TaskThumb.Visibility = Visibility.Visible;
        TaskIconCircle.Visibility = Visibility.Collapsed;
    }

    /// <summary>The GIF message the notification is about; a click on its picture opens the chat.</summary>
    (Friend Friend, string Id)? _taskGif;

    void OnReactionReceived(Friend friend, string messageId, string? emoji)
    {
        if (!_chats.SetReaction(friend.Key, messageId, mine: false, emoji)) return;
        Diag.Log("Chat reaction from a friend");
        if (emoji == null || Reading(friend) || !_settings.ChatPopups) return;
        if (_chats.Find(friend.Key, messageId) is not { Mine: true }) return; // only reactions to what we wrote are news
        ShowPeek(null, $"{friend.Name} reacted {emoji}", "", null, false, null, seconds: 3);
    }

    /// <summary>Our reaction to a message: set at once on this PC, then sent to the friend (tried twice).</summary>
    void ReactTo(Friend friend, FriendMessage message, string? emoji)
    {
        string? clean = ChatReaction.Clean(emoji);
        if (!_chats.SetReaction(friend.Key, message.Id, mine: true, clean)) return;
        if (_demoFriends != null) return; // snapshots
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

    /// <summary>Keeps the GIF among the recent ones in the picker (the link only).</summary>
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
        // A pasted GIF link goes as the GIF itself.
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

    void RetryChat(Friend friend, FriendMessage message)
    {
        _chats.SetState(friend.Key, message.Id, ChatState.Sending);
        _ = DeliverAsync(friend, message);
    }

    async Task DeliverAsync(Friend friend, FriendMessage message)
    {
        if (_demoFriends != null) return; // snapshots
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

    void MenuChatPopups_Click(object sender, RoutedEventArgs e)
    {
        _settings.ChatPopups = MenuChatPopups.IsChecked;
        _settings.Save();
    }

    void MenuFriendsHub_Click(object sender, RoutedEventArgs e) => Dispatcher.InvokeAsync(() => OpenChat(null), System.Windows.Threading.DispatcherPriority.Background);
}
