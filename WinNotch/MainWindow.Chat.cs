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
        _friends.RemovedUs += friend => _chats.Forget(friend.Key);
    }

    /// <summary>Whether the Holm window is in front, showing the chat with this friend.</summary>
    bool Reading(Friend friend)
        => _settingsWindow is { IsActive: true, ShownView: ChatPage page } && page.Friend?.Key == friend.Key;

    void OnChatReceived(Friend friend, string id, string text, long sent)
    {
        bool reading = Reading(friend);
        var message = new FriendMessage { Id = id, Mine = false, Text = text, Sent = sent, State = ChatState.Received };
        if (!_chats.Add(friend.Key, message, unread: !reading)) return; // a note seen before
        Diag.Log("Chat message from a friend");
        if (reading || !_settings.ChatPopups) return;
        if (_busy || _taskVisible || _sharing || _installingUpdate || _hiddenForFullscreen)
        {
            ShowPeek(null, $"Message from {friend.Name}", "", null, false, null, seconds: 4);
            return;
        }
        string preview = text.Replace('\n', ' ');
        if (preview.Length > 80) preview = preview[..80] + "…";
        ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), friend.Name, preview, null,
                 ("Reply", () => { HideTask(); OpenChat(friend); }), ("Close", HideTask), textIcon: true);
        _taskHide.Interval = TimeSpan.FromSeconds(10);
        _taskHide.Start();
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
            await _friends.SendChatAsync(friend, message.Id, message.Text, message.Sent, cts.Token);
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
