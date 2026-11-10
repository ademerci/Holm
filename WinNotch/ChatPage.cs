using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinNotch.Sharing;
using EmojiText = Emoji.Wpf.TextBlock;

namespace WinNotch;

/// <summary>What the friends pages of the Holm window need from the notch: friends, faces, chats and the ways to reach them.</summary>
internal sealed class SocialContext
{
    public required Func<IReadOnlyList<Friend>> Friends { get; init; }
    public required ChatStore Chats { get; init; }
    /// <summary>A friend's initials (empty over a picture) and fill, by name.</summary>
    public required Func<string, (string Text, Brush Fill)> Face { get; init; }
    public required Func<Friend, bool> IsOnline { get; init; }
    /// <summary>Sends a chat text (split into several messages if long); the store shows how it goes.</summary>
    public required Action<Friend, string> Send { get; init; }
    /// <summary>Sends a message that didn't go again.</summary>
    public required Action<Friend, FriendMessage> Retry { get; init; }
    /// <summary>Opens another page of the Holm window, with a friend chosen (the chat with them, say).</summary>
    public Action<string, Friend?>? Open { get; init; }
    /// <summary>What a friend is doing, if they're online and share it.</summary>
    public Func<Friend, FriendActivity?> ActivityOf { get; init; } = _ => null;
    /// <summary>What we're doing, as friends see it (null: nothing, or sharing is off).</summary>
    public Func<FriendActivity?> Mine { get; init; } = () => null;
    /// <summary>Whether we share our activity at all.</summary>
    public Func<bool> Sharing { get; init; } = () => false;
    /// <summary>Asks friends whether their Holm runs; the answers bring what they're doing.</summary>
    public Action Ping { get; init; } = () => { };
    public Action<Friend> Wave { get; init; } = _ => { };
    /// <summary>Invites a friend to watch our screen; null where streaming isn't offered (snapshots).</summary>
    public Action<Friend>? Stream { get; init; }
    /// <summary>Sends a GIF (its link).</summary>
    public Action<Friend, string> SendGif { get; init; } = (_, _) => { };
    /// <summary>Reacts to a message with an emoji, or takes our reaction away (null).</summary>
    public Action<Friend, FriendMessage, string?> React { get; init; } = (_, _, _) => { };
    /// <summary>The quick reactions in the reaction bar, in order.</summary>
    public Func<IReadOnlyList<string>> QuickReactions { get; init; } = () => ChatReaction.Defaults;
    /// <summary>Saves new quick reactions.</summary>
    public Action<IReadOnlyList<string>> SetQuickReactions { get; init; } = _ => { };
    /// <summary>GIFs sent or received lately, newest first.</summary>
    public Func<IReadOnlyList<string>> RecentGifs { get; init; } = () => Array.Empty<string>();
    /// <summary>The key GIF search uses; null when there is none.</summary>
    public Func<string?> GifKey { get; init; } = () => null;
    /// <summary>Saves a GIPHY key typed into the picker.</summary>
    public Action<string?> SetGifKey { get; init; } = _ => { };
    /// <summary>Whether GIFs move (off with Animations).</summary>
    public Func<bool> Animate { get; init; } = () => true;
    /// <summary>How many achievements a friend has, if their notch said.</summary>
    public Func<Friend, int?> TrophiesOf { get; init; } = _ => null;

    /// <summary>A friend came online, went quiet, changed their profile or what they're doing.</summary>
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}

/// <summary>Chat: your friends on the left, the conversation with one of them on the right.</summary>
internal sealed class ChatPage : HubView
{
    readonly SocialContext _social;
    readonly StackPanel _list = new(), _messages = new();
    readonly ScrollViewer _scroll = null!;
    readonly Grid _conversation = null!;
    readonly Border _header = new();
    readonly TextBox _input = null!;
    readonly TextBlock _counter = null!;
    Friend? _friend;
    /// <summary>The message just reacted to here: its reaction pops in, like Instagram's heart.</summary>
    string? _justReacted;

    public ChatPage(SocialContext social, Friend? with)
    {
        _social = social;
        var friends = social.Friends();
        if (friends.Count == 0)
        {
            Child = Empty("", "No friends yet", "Add a friend with Friends › Add friend from clipboard in the notch's right-click menu, then chat here.");
            return;
        }

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.Children.Add(new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Child = HubTheme.Scroller(_list) });

        _conversation = new Grid();
        _conversation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _conversation.RowDefinitions.Add(new RowDefinition());
        _conversation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _header.Padding = new Thickness(16, 12, 16, 12);
        _header.BorderBrush = HubTheme.Line;
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        _conversation.Children.Add(_header);
        _messages.Margin = new Thickness(16, 12, 10, 12);
        _scroll = HubTheme.Scroller(_messages);
        Grid.SetRow(_scroll, 1);
        _conversation.Children.Add(_scroll);

        var inputRow = new Grid { Margin = new Thickness(12, 0, 12, 12) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition());
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _input = HubTheme.Field("Message", out var host, multiline: true);
        _input.MaxHeight = 110;
        _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _input.PreviewKeyDown += (_, e) =>
        {
            // Enter sends, Shift+Enter starts a new line.
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            SendInput();
        };
        _counter = new TextBlock { FontSize = 11, Foreground = HubTheme.Faint, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        host.Children.Add(_counter);
        _input.TextChanged += (_, _) =>
        {
            int parts = _input.Text.Trim().Length == 0 ? 0 : ChatText.Split(_input.Text).Count;
            _counter.Text = parts > 1 ? $"{parts} messages" : "";
        };
        inputRow.Children.Add(host);
        var gifButton = HubTheme.Action("GIF");
        gifButton.Margin = new Thickness(10, 0, 0, 0);
        gifButton.VerticalAlignment = VerticalAlignment.Bottom;
        gifButton.ToolTip = "Send a GIF";
        gifButton.Click += (_, _) =>
        {
            if (_friend is not { } to) return;
            ChatExtras.ShowGifPicker(gifButton, _social, url => { _social.SendGif(to, url); _input.Focus(); });
        };
        Grid.SetColumn(gifButton, 1);
        inputRow.Children.Add(gifButton);
        var send = HubTheme.Action("Send", primary: true);
        send.Margin = new Thickness(8, 0, 0, 0);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.Click += (_, _) => SendInput();
        Grid.SetColumn(send, 2);
        inputRow.Children.Add(send);
        Grid.SetRow(inputRow, 2);
        _conversation.Children.Add(inputRow);

        var right = new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Child = _conversation };
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);
        Child = layout;

        social.Chats.Changed += OnChanged;
        social.Changed += OnFriendsChanged;
        // Unread first, then whoever wrote last.
        Select(with ?? friends.OrderByDescending(f => social.Chats.Unread(f.Key)).ThenByDescending(f => social.Chats.Last(f.Key)?.Sent ?? 0).First());
        Loaded += (_, _) => _input.Focus();
    }

    /// <summary>The friend whose conversation is shown.</summary>
    public Friend? Friend => _friend;

    public override void Detach()
    {
        _social.Chats.Changed -= OnChanged;
        _social.Changed -= OnFriendsChanged;
    }

    /// <summary>Someone came online or changed their picture or status.</summary>
    void OnFriendsChanged()
    {
        ShowHeader();
        ShowList();
    }

    /// <summary>Marks the shown conversation read when the window comes forward.</summary>
    public void Seen()
    {
        if (_friend != null && Window.GetWindow(this)?.IsActive == true) _social.Chats.MarkRead(_friend.Key);
    }

    void OnChanged(string key)
    {
        if (_friend?.Key == key)
        {
            // A reaction to an older message mustn't jump to the end; a new message at the end does.
            bool atEnd = _scroll.VerticalOffset >= _scroll.ScrollableHeight - 24;
            double offset = _scroll.VerticalOffset;
            ShowMessages(scrollToEnd: atEnd);
            if (!atEnd) _scroll.ScrollToVerticalOffset(offset);
            Seen();
        }
        ShowList();
    }

    public void Select(Friend friend)
    {
        _friend = _social.Friends().FirstOrDefault(f => f.Key == friend.Key) ?? friend;
        ShowHeader();
        ShowMessages();
        _social.Chats.MarkRead(_friend.Key); // the page is open on them
        ShowList();
        _input.Focus();
    }

    void SendInput()
    {
        if (_friend == null || _input.Text.Trim().Length == 0) return;
        _social.Send(_friend, _input.Text);
        _input.Clear();
    }

    void ShowList()
    {
        _list.Children.Clear();
        foreach (var friend in _social.Friends().OrderByDescending(f => _social.Chats.Last(f.Key)?.Sent ?? 0).ThenBy(f => f.Name))
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(FaceWithDot(friend, 36));
            var words = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            int unread = _social.Chats.Unread(friend.Key);
            words.Children.Add(new TextBlock { Text = friend.Name, FontWeight = unread > 0 ? FontWeights.Bold : FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var last = _social.Chats.Last(friend.Key);
            if (last?.Gif is { } gif)
            {
                // A GIF shows as a small moving picture in the list, like a preview should.
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                line.Children.Add(GifPlayer.View(gif, 28, 20, 4, _social.Animate(), maxSide: 64));
                line.Children.Add(new TextBlock
                {
                    Text = (last.Mine ? "You: " : "") + "GIF", FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Foreground = unread > 0 ? Brushes.White : HubTheme.Dim,
                });
                words.Children.Add(line);
            }
            else
            {
                string preview = last == null ? friend.Status ?? "Say hi" : (last.Mine ? "You: " : "") + last.Text.Replace('\n', ' ');
                words.Children.Add(new TextBlock { Text = preview, FontSize = 12, Foreground = unread > 0 ? Brushes.White : HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
            }
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            if (unread > 0)
            {
                var badge = new Border
                {
                    Background = HubTheme.Red, CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = unread.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                };
                Grid.SetColumn(badge, 2);
                row.Children.Add(badge);
            }
            bool selected = friend.Key == _friend?.Key;
            var item = new Border
            {
                Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Child = row,
                Background = selected ? HubTheme.Selected : Brushes.Transparent, Margin = new Thickness(0, 0, 0, 2),
            };
            item.MouseEnter += (_, _) => { if (!selected) item.Background = HubTheme.Hover; };
            item.MouseLeave += (_, _) => { if (!selected) item.Background = Brushes.Transparent; };
            item.MouseLeftButtonUp += (_, _) => Select(friend);
            _list.Children.Add(item);
        }
    }

    void ShowHeader()
    {
        if (_friend == null) return;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(FaceWithDot(_friend, 40));
        var friend = _friend;
        var stream = HubTheme.Action("Share my screen");
        stream.VerticalAlignment = VerticalAlignment.Center;
        stream.ToolTip = $"Invite {friend.Name} to watch your screen";
        stream.Click += (_, _) => _social.Stream?.Invoke(friend);
        Grid.SetColumn(stream, 2);
        row.Children.Add(stream);
        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = _friend.Name, FontSize = 16, FontWeight = FontWeights.SemiBold });
        bool online = _social.IsOnline(_friend);
        string line = (online ? "Online" : "Messages wait in their inbox for about 12 hours")
                      + (_friend.Status != null ? " · " + _friend.Status : "");
        words.Children.Add(new TextBlock { Text = line, FontSize = 12, Foreground = online ? HubTheme.Green : HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        _header.Child = row;
    }

    void ShowMessages(bool scrollToEnd = true)
    {
        _messages.Children.Clear();
        if (_friend == null) return;
        var messages = _social.Chats.With(_friend.Key);
        if (messages.Count == 0)
        {
            _messages.Children.Add(new TextBlock
            {
                Text = $"This is the start of your chat with {_friend.Name}. Messages are sealed so only the two of you can read them.",
                Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(40, 30, 40, 0),
            });
            return;
        }
        DateTime? lastDay = null;
        FriendMessage? previous = null;
        foreach (var message in messages)
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(message.Sent).LocalDateTime;
            if (lastDay != at.Date)
            {
                lastDay = at.Date;
                previous = null;
                _messages.Children.Add(new TextBlock
                {
                    Text = DayLabel(at.Date), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = HubTheme.Faint,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 8),
                });
            }
            // Messages close together from the same side sit closer and share one time.
            bool grouped = previous != null && previous.Mine == message.Mine && message.Sent - previous.Sent < 180;
            _messages.Children.Add(Bubble(message, at, grouped));
            previous = message;
        }
        _justReacted = null;
        _scroll.UpdateLayout();
        if (scrollToEnd) _scroll.ScrollToEnd();
    }

    /// <summary>Our reaction to a message: given, changed or (null) taken away.</summary>
    void React(FriendMessage message, string? emoji)
    {
        if (_friend == null) return;
        _justReacted = emoji == null ? null : message.Id;
        _social.React(_friend, message, emoji);
    }

    /// <summary>
    /// The reactions under a bubble, as on Instagram: a small pill over its lower edge with both sides' emoji (and a 2
    /// when both reacted). A click on it takes ours away.
    /// </summary>
    FrameworkElement? Reactions(FriendMessage message)
    {
        var emoji = new[] { message.TheirReaction, message.MyReaction }.Where(e => e != null).Distinct().ToList();
        if (emoji.Count == 0) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string e in emoji!) row.Children.Add(ChatExtras.Emoji(e!, 13));
        int count = (message.TheirReaction != null ? 1 : 0) + (message.MyReaction != null ? 1 : 0);
        if (count > 1) row.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 11, Foreground = HubTheme.Dim, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var who = new List<string>();
        if (message.MyReaction != null) who.Add($"You {message.MyReaction} · click to remove");
        if (message.TheirReaction != null) who.Add($"{_friend?.Name} {message.TheirReaction}");
        var scale = new ScaleTransform(1, 1);
        var pill = new Border
        {
            Background = HubTheme.Frozen(Color.FromRgb(0x2C, 0x2C, 0x2E)), BorderBrush = HubTheme.Frozen(Color.FromRgb(0x1E, 0x1E, 0x20)),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Padding = new Thickness(5, 1, 6, 2), Child = row,
            HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = message.Mine ? new Thickness(0, -9, 8, 0) : new Thickness(8, -9, 0, 0),
            Cursor = message.MyReaction != null ? Cursors.Hand : Cursors.Arrow, ToolTip = string.Join("\n", who),
            RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5),
        };
        pill.MouseLeftButtonUp += (_, e) =>
        {
            if (message.MyReaction == null) return;
            e.Handled = true;
            React(message, null);
        };
        if (_justReacted == message.Id && _social.Animate())
        {
            var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        return pill;
    }

    FrameworkElement Bubble(FriendMessage message, DateTime at, bool grouped)
    {
        FrameworkElement content;
        bool gif = message.Gif != null;
        if (gif)
            content = GifPlayer.View(message.Gif!, 220, 160, 14, _social.Animate(), maxSide: 320, fitHeight: true);
        else
            content = new EmojiText { Text = message.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
        var bubble = new Border
        {
            Background = gif ? Brushes.Transparent : message.Mine ? HubTheme.Accent : HubTheme.CardStrong,
            CornerRadius = message.Mine ? new CornerRadius(16, 16, 4, 16) : new CornerRadius(16, 16, 16, 4),
            Padding = gif ? new Thickness(0) : new Thickness(12, 7, 12, 8), MaxWidth = 420, Child = content,
            Opacity = message.State == ChatState.Sending ? 0.6 : 1,
            ToolTip = at.ToString("g", CultureInfo.CurrentCulture),
        };
        // A double click gives a heart (or takes it away), like a double tap on Instagram.
        bubble.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            e.Handled = true;
            React(message, message.MyReaction == ChatReaction.DoubleClick ? null : ChatReaction.DoubleClick);
        };
        // Copy, as from any chat, and React.
        var copy = new MenuItem { Header = gif ? "Copy GIF link" : "Copy" };
        copy.Click += (_, _) => { try { Clipboard.SetText(message.Gif ?? message.Text); } catch (System.Runtime.InteropServices.COMException) { } };
        var reactItem = new MenuItem { Header = "React…" };
        reactItem.Click += (_, _) => ChatExtras.ShowReactionBar(bubble, _social, message.MyReaction, emoji => React(message, emoji));
        bubble.ContextMenu = new ContextMenu { Items = { reactItem, copy } };

        // The smiley beside the bubble on hover opens the reaction bar.
        var smiley = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand, Background = HubTheme.Card,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Opacity = 0, ToolTip = "React",
            Child = HubTheme.Glyph("\uE76E", 14, HubTheme.Dim),
        };
        smiley.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ChatExtras.ShowReactionBar(bubble, _social, message.MyReaction, emoji => React(message, emoji));
        };

        var withReactions = new StackPanel();
        withReactions.Children.Add(bubble);
        if (Reactions(message) is { } pill) withReactions.Children.Add(pill);

        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, Background = Brushes.Transparent };
        if (message.Mine) { line.Children.Add(smiley); line.Children.Add(withReactions); }
        else { line.Children.Add(withReactions); line.Children.Add(smiley); }
        line.MouseEnter += (_, _) => smiley.Opacity = 1;
        line.MouseLeave += (_, _) => smiley.Opacity = 0;

        var stack = new StackPanel
        {
            HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(0, grouped ? 2 : 8, 0, 0),
        };
        stack.Children.Add(line);
        if (message.State == ChatState.Failed)
        {
            var retry = new TextBlock
            {
                Text = "Not sent · Click to try again", FontSize = 11, Foreground = HubTheme.Red, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 4, 0),
            };
            retry.MouseLeftButtonUp += (_, _) => { if (_friend != null) _social.Retry(_friend, message); };
            stack.Children.Add(retry);
        }
        else if (!grouped || message.State == ChatState.Sending)
        {
            stack.Children.Add(new TextBlock
            {
                Text = message.State == ChatState.Sending ? "Sending…" : at.ToString("t", CultureInfo.CurrentCulture),
                FontSize = 10.5, Foreground = HubTheme.Faint, Margin = new Thickness(4, 3, 4, 0),
                HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            });
        }
        return stack;
    }

    static string DayLabel(DateTime day)
        => day == DateTime.Today ? "Today" : day == DateTime.Today.AddDays(-1) ? "Yesterday" : day.ToString("D", CultureInfo.CurrentCulture);

    FrameworkElement FaceWithDot(Friend friend, double size)
    {
        var (text, fill) = _social.Face(friend.Name);
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(HubTheme.Face(text, fill, size));
        if (_social.IsOnline(friend))
            grid.Children.Add(new Border
            {
                Width = size * 0.32, Height = size * 0.32, CornerRadius = new CornerRadius(size * 0.16), Background = HubTheme.Green,
                BorderBrush = HubTheme.Frozen(Color.FromRgb(0x26, 0x26, 0x28)), BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            });
        return grid;
    }

    /// <summary>A page with nothing in it yet: a big icon and a line of what to do.</summary>
    public static FrameworkElement Empty(string glyph, string title, string text)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380 };
        stack.Children.Add(new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = HubTheme.Card, Child = HubTheme.Glyph(glyph, 26, HubTheme.Dim),
            Margin = new Thickness(0, 0, 0, 16),
        });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = text, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
        return stack;
    }
}
