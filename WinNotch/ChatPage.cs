using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Sharing;

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
        var send = HubTheme.Action("Send", primary: true);
        send.Margin = new Thickness(10, 0, 0, 0);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.Click += (_, _) => SendInput();
        Grid.SetColumn(send, 1);
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
            ShowMessages();
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
            string preview = last == null ? friend.Status ?? "Say hi" : (last.Mine ? "You: " : "") + last.Text.Replace('\n', ' ');
            words.Children.Add(new TextBlock { Text = preview, FontSize = 12, Foreground = unread > 0 ? Brushes.White : HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
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

    void ShowMessages()
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
        _scroll.UpdateLayout();
        _scroll.ScrollToEnd();
    }

    FrameworkElement Bubble(FriendMessage message, DateTime at, bool grouped)
    {
        var text = new TextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
        var bubble = new Border
        {
            Background = message.Mine ? HubTheme.Accent : HubTheme.CardStrong,
            CornerRadius = message.Mine ? new CornerRadius(16, 16, 4, 16) : new CornerRadius(16, 16, 16, 4),
            Padding = new Thickness(12, 7, 12, 8), MaxWidth = 420, Child = text,
            Opacity = message.State == ChatState.Sending ? 0.6 : 1,
            ToolTip = at.ToString("g", CultureInfo.CurrentCulture),
        };
        // Copy, as from any chat.
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => { try { Clipboard.SetText(message.Text); } catch (System.Runtime.InteropServices.COMException) { } };
        bubble.ContextMenu = new ContextMenu { Items = { copy } };

        var stack = new StackPanel
        {
            HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(0, grouped ? 2 : 8, 0, 0),
        };
        stack.Children.Add(bubble);
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
