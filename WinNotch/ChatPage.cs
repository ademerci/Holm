using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// <summary>Sends selected files to a friend using Beam.</summary>
    public Action<Friend, string[]> SendFiles { get; init; } = (_, _) => { };
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

/// <summary>
/// Chat, as in the messengers people know: the conversations on the left (searchable, newest first), the one you're in
/// on the right with bubbles that run together, big emoji, reactions, GIFs, and a round message box at the bottom.
/// </summary>
internal sealed class ChatPage : HubView
{
    // The page's own shades: the list a little lighter than the conversation, bubbles that stand out from both.
    static readonly Brush ListBack = HubTheme.Frozen(Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF));
    static readonly Brush ConversationBack = HubTheme.Frozen(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
    static readonly Brush TheirBubble = HubTheme.Frozen(Color.FromRgb(0x2E, 0x2E, 0x31));
    static readonly Brush Composer = HubTheme.Frozen(Color.FromRgb(0x2A, 0x2A, 0x2D));
    static readonly Brush ComposerEdge = HubTheme.Frozen(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
    static readonly Brush MyBubble = MakeMyBubble();
    static readonly Brush DotEdge = HubTheme.Frozen(Color.FromRgb(0x22, 0x22, 0x24));

    /// <summary>Messages this close together from the same side run together in one group.</summary>
    const long GroupSeconds = 180;
    /// <summary>After a pause this long the time shows again between the messages.</summary>
    const long TimeGapSeconds = 15 * 60;

    readonly SocialContext _social;
    readonly StackPanel _list = new(), _messages = new();
    readonly ScrollViewer _scroll = null!;
    readonly Border _header = new();
    readonly TextBox _input = null!, _search = null!;
    readonly TextBlock _counter = null!;
    readonly Border _send = null!;
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

        // One card, split in two: the conversations and the one you're in.
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());

        // ── Left: search and conversations ──
        var left = new Grid { Background = ListBack };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());
        _search = HubTheme.Field("Search", out var searchHost);
        _search.Padding = new Thickness(26, 4, 6, 4);
        _search.Background = HubTheme.Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        _search.BorderBrush = Brushes.Transparent;
        ((TextBlock)searchHost.Children[1]).Margin = new Thickness(32, 0, 0, 0);
        var magnifier = HubTheme.Glyph("", 12, HubTheme.Faint);
        magnifier.HorizontalAlignment = HorizontalAlignment.Left;
        magnifier.Margin = new Thickness(12, 0, 0, 0);
        magnifier.IsHitTestVisible = false;
        searchHost.Children.Add(magnifier);
        searchHost.Margin = new Thickness(12, 12, 12, 8);
        _search.TextChanged += (_, _) => ShowList();
        left.Children.Add(searchHost);
        _list.Margin = new Thickness(8, 0, 2, 8);
        var listScroll = HubTheme.Scroller(_list);
        Grid.SetRow(listScroll, 1);
        left.Children.Add(listScroll);
        layout.Children.Add(left);

        var divider = new Border { Background = HubTheme.Line };
        Grid.SetColumn(divider, 1);
        layout.Children.Add(divider);

        // ── Right: header, messages, message box ──
        var conversation = new Grid { Background = ConversationBack };
        conversation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        conversation.RowDefinitions.Add(new RowDefinition());
        conversation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _header.Padding = new Thickness(16, 10, 12, 10);
        _header.BorderBrush = HubTheme.Line;
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        _header.Background = ListBack;
        conversation.Children.Add(_header);
        _messages.Margin = new Thickness(14, 6, 12, 10);
        _scroll = HubTheme.Scroller(_messages);
        Grid.SetRow(_scroll, 1);
        conversation.Children.Add(_scroll);

        // The message box: attachments on the left, GIF and send on the right.
        var box = new Grid();
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        box.ColumnDefinitions.Add(new ColumnDefinition());
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var attach = new Border
        {
            Width = 30, Height = 30, Margin = new Thickness(5, 0, 0, 0), CornerRadius = new CornerRadius(15),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Send files",
            VerticalAlignment = VerticalAlignment.Center, Child = HubTheme.Glyph("\uE710", 14, HubTheme.Dim),
        };
        attach.MouseEnter += (_, _) => attach.Background = HubTheme.Hover;
        attach.MouseLeave += (_, _) => attach.Background = Brushes.Transparent;
        attach.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_friend is not { } to) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose files to send", Multiselect = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) _social.SendFiles(to, dialog.FileNames);
            _input.Focus();
        };
        box.Children.Add(attach);
        var gif = Chip("GIF", "Send a GIF");
        gif.Margin = new Thickness(2, 0, 2, 0);
        gif.VerticalAlignment = VerticalAlignment.Center;
        gif.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_friend is not { } to) return;
            ChatExtras.ShowGifPicker(gif, _social, url => { _social.SendGif(to, url); _input.Focus(); });
        };
        Grid.SetColumn(gif, 2);
        box.Children.Add(gif);
        _input = HubTheme.Field("Message…", out var inputHost, multiline: true);
        _input.Background = Brushes.Transparent;
        _input.BorderThickness = new Thickness(0);
        _input.Padding = new Thickness(2, 3, 4, 3);
        _input.FontSize = 13.5;
        _input.MaxHeight = 120;
        _input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        ((TextBlock)inputHost.Children[1]).Margin = new Thickness(2, 0, 0, 0);
        _input.PreviewKeyDown += (_, e) =>
        {
            // Enter sends, Shift+Enter starts a new line.
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            SendInput();
        };
        _counter = new TextBlock { FontSize = 10.5, Foreground = HubTheme.Faint, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -14, 6, 0), IsHitTestVisible = false };
        inputHost.Children.Add(_counter);
        Grid.SetColumn(inputHost, 1);
        box.Children.Add(inputHost);
        _send = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(4, 4, 5, 4), VerticalAlignment = VerticalAlignment.Bottom,
            Background = HubTheme.Accent, Cursor = Cursors.Hand, ToolTip = "Send (Enter)", Child = HubTheme.Glyph("", 13),
        };
        _send.MouseLeftButtonUp += (_, e) => { e.Handled = true; SendInput(); };
        Grid.SetColumn(_send, 3);
        box.Children.Add(_send);
        _input.TextChanged += (_, _) => RefreshComposer();
        var composer = new Border
        {
            Background = Composer, BorderBrush = ComposerEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(21),
            MinHeight = 42, Margin = new Thickness(14, 6, 14, 14), Child = box,
        };
        Grid.SetRow(composer, 2);
        conversation.Children.Add(composer);
        Grid.SetColumn(conversation, 2);
        layout.Children.Add(conversation);

        Child = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, BorderBrush = HubTheme.Line, BorderThickness = new Thickness(1), Child = layout };
        Loaded += (_, _) => Child.Clip = new RectangleGeometry(new Rect(0, 0, ((Border)Child).ActualWidth, ((Border)Child).ActualHeight), 14, 14);
        SizeChanged += (_, _) => Child.Clip = new RectangleGeometry(new Rect(0, 0, ((Border)Child).ActualWidth, ((Border)Child).ActualHeight), 14, 14);

        social.Chats.Changed += OnChanged;
        social.Changed += OnFriendsChanged;
        RefreshComposer();
        // Unread first, then whoever wrote last.
        Select(with ?? friends.OrderByDescending(f => social.Chats.Unread(f.Key)).ThenByDescending(f => social.Chats.Last(f.Key)?.Sent ?? 0).First());
        Loaded += (_, _) => _input.Focus();
    }

    static Brush MakeMyBubble()
    {
        var brush = new LinearGradientBrush(Color.FromRgb(0x2E, 0x7B, 0xFF), Color.FromRgb(0x7B, 0x4D, 0xFF), new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
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

    /// <summary>The send button lights up when there's something to send; a long text says how many messages it makes.</summary>
    void RefreshComposer()
    {
        bool any = _input.Text.Trim().Length > 0;
        _send.Opacity = any ? 1 : 0.35;
        _send.Cursor = any ? Cursors.Hand : Cursors.Arrow;
        int parts = any ? ChatText.Split(_input.Text).Count : 0;
        _counter.Text = parts > 1 ? $"{parts} messages" : "";
    }

    // ───────────────────────── the conversations ─────────────────────────

    void ShowList()
    {
        _list.Children.Clear();
        string filter = _search.Text.Trim();
        var friends = _social.Friends()
            .Where(f => filter.Length == 0 || f.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(f => _social.Chats.Last(f.Key)?.Sent ?? 0).ThenBy(f => f.Name)
            .ToList();
        if (friends.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = $"No one called “{filter}”", Foreground = HubTheme.Dim, FontSize = 12, Margin = new Thickness(10, 12, 10, 0), TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var friend in friends) _list.Children.Add(Row(friend));
    }

    FrameworkElement Row(Friend friend)
    {
        int unread = _social.Chats.Unread(friend.Key);
        var last = _social.Chats.Last(friend.Key);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(FaceWithDot(friend, 44));

        var words = new Grid { Margin = new Thickness(11, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        words.ColumnDefinitions.Add(new ColumnDefinition());
        words.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        words.RowDefinitions.Add(new RowDefinition());
        words.RowDefinitions.Add(new RowDefinition());
        words.Children.Add(new TextBlock { Text = friend.Name, FontSize = 13.5, FontWeight = unread > 0 ? FontWeights.Bold : FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (last != null)
        {
            var when = new TextBlock
            {
                Text = ListTime(DateTimeOffset.FromUnixTimeSeconds(last.Sent).LocalDateTime), FontSize = 11, Margin = new Thickness(8, 1, 0, 0),
                Foreground = unread > 0 ? HubTheme.Accent : HubTheme.Faint, FontWeight = unread > 0 ? FontWeights.SemiBold : FontWeights.Normal,
            };
            Grid.SetColumn(when, 1);
            words.Children.Add(when);
        }

        FrameworkElement preview;
        Brush previewColor = unread > 0 ? Brushes.White : HubTheme.Dim;
        if (last?.Gif is { } gif)
        {
            // A GIF shows as a small moving picture in the list, like a preview should.
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(GifPlayer.View(gif, 24, 16, 4, _social.Animate(), maxSide: 48));
            line.Children.Add(new TextBlock { Text = (last.Mine ? "You: " : "") + "GIF", FontSize = 12, Margin = new Thickness(6, 0, 0, 0), Foreground = previewColor, VerticalAlignment = VerticalAlignment.Center });
            preview = line;
        }
        else
        {
            string text = last == null ? friend.Status ?? (_social.IsOnline(friend) ? "Active now" : "Say hi 👋")
                        : (last.Mine ? "You: " : "") + last.Text.Replace('\n', ' ');
            preview = new EmojiText { Text = text, FontSize = 12, Foreground = previewColor, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = unread > 0 ? FontWeights.SemiBold : FontWeights.Normal };
        }
        preview.Margin = new Thickness(0, 3, 0, 0);
        Grid.SetRow(preview, 1);
        words.Children.Add(preview);
        if (unread > 0)
        {
            var badge = new Border
            {
                Background = HubTheme.Accent, CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(8, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = unread > 99 ? "99+" : unread.ToString(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Grid.SetRow(badge, 1);
            Grid.SetColumn(badge, 1);
            words.Children.Add(badge);
        }
        Grid.SetColumn(words, 1);
        row.Children.Add(words);

        bool selected = friend.Key == _friend?.Key;
        var item = new Border
        {
            Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Child = row,
            Background = selected ? HubTheme.Selected : Brushes.Transparent, Margin = new Thickness(0, 0, 0, 1),
        };
        item.MouseEnter += (_, _) => { if (!selected) item.Background = HubTheme.Hover; };
        item.MouseLeave += (_, _) => { if (!selected) item.Background = Brushes.Transparent; };
        item.MouseLeftButtonUp += (_, _) => Select(friend);
        return item;
    }

    /// <summary>When the last message came, as short as the list has room for: 17:42, Mon, 3 Nov.</summary>
    static string ListTime(DateTime at)
        => at.Date == DateTime.Today ? at.ToString("t", CultureInfo.CurrentCulture)
         : at.Date > DateTime.Today.AddDays(-7) ? at.ToString("ddd", CultureInfo.CurrentCulture)
         : at.ToString("d MMM", CultureInfo.CurrentCulture);

    // ───────────────────────── the one you're in ─────────────────────────

    void ShowHeader()
    {
        if (_friend == null) return;
        var friend = _friend;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(FaceWithDot(friend, 38));
        var words = new StackPanel { Margin = new Thickness(11, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = friend.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        bool online = _social.IsOnline(friend);
        string line = online ? "Active now" : "Offline · messages wait about 12 hours";
        if (friend.Status != null) line += " · " + friend.Status;
        words.Children.Add(new EmojiText { Text = line, FontSize = 11.5, Foreground = online ? HubTheme.Green : HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var wave = IconButton("\U0001F44B", $"Wave at {friend.Name}", emoji: true);
        wave.MouseLeftButtonUp += (_, e) => { e.Handled = true; _social.Wave(friend); };
        actions.Children.Add(wave);
        if (_social.Stream is { } stream)
        {
            var share = IconButton("", $"Share your screen with {friend.Name}");
            share.MouseLeftButtonUp += (_, e) => { e.Handled = true; stream(friend); };
            actions.Children.Add(share);
        }
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        _header.Child = row;
    }

    void ShowMessages(bool scrollToEnd = true)
    {
        _messages.Children.Clear();
        if (_friend == null) return;
        var messages = _social.Chats.With(_friend.Key);
        if (messages.Count == 0)
        {
            _messages.Children.Add(Welcome(_friend));
            return;
        }
        // The face and the friend's name at the top, as messengers start a conversation.
        _messages.Children.Add(Welcome(_friend, small: true));
        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var previous = i > 0 ? messages[i - 1] : null;
            var next = i + 1 < messages.Count ? messages[i + 1] : null;
            bool newTime = previous == null || message.Sent - previous.Sent >= TimeGapSeconds
                           || DateTimeOffset.FromUnixTimeSeconds(previous.Sent).LocalDateTime.Date != DateTimeOffset.FromUnixTimeSeconds(message.Sent).LocalDateTime.Date;
            if (newTime) _messages.Children.Add(TimeLabel(DateTimeOffset.FromUnixTimeSeconds(message.Sent).LocalDateTime));
            bool joinsPrevious = !newTime && Together(previous, message);
            bool joinsNext = next != null && Together(message, next) && next.Sent - message.Sent < TimeGapSeconds;
            bool lastOfMine = message.Mine && next == null;
            _messages.Children.Add(Bubble(message, joinsPrevious, joinsNext, lastOfMine));
        }
        _justReacted = null;
        _scroll.UpdateLayout();
        if (scrollToEnd) _scroll.ScrollToEnd();
    }

    static bool Together(FriendMessage? a, FriendMessage? b)
        => a != null && b != null && a.Mine == b.Mine && b.Sent - a.Sent < GroupSeconds
           && a.MyReaction == null && a.TheirReaction == null; // a reaction pill needs room under its bubble

    /// <summary>The top of a conversation: their face, name and a word on how private it is (or a way to start).</summary>
    FrameworkElement Welcome(Friend friend, bool small = false)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, small ? 18 : 40, 20, small ? 10 : 0) };
        var face = FaceWithDot(friend, small ? 56 : 84);
        face.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(face);
        stack.Children.Add(new TextBlock { Text = friend.Name, FontSize = small ? 15 : 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        stack.Children.Add(new TextBlock
        {
            Text = "Messages are sealed so only the two of you can read them.", FontSize = 11.5, Foreground = HubTheme.Faint,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        });
        if (!small)
        {
            var hi = Chip("Say hi 👋", $"Send {friend.Name} a wave");
            hi.HorizontalAlignment = HorizontalAlignment.Center;
            hi.Margin = new Thickness(0, 16, 0, 0);
            hi.MouseLeftButtonUp += (_, e) => { e.Handled = true; _social.Send(friend, "👋"); };
            stack.Children.Add(hi);
        }
        return stack;
    }

    static TextBlock TimeLabel(DateTime at) => new()
    {
        Text = (at.Date == DateTime.Today ? "Today" : at.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : at.ToString("ddd d MMM", CultureInfo.CurrentCulture))
               + " " + at.ToString("t", CultureInfo.CurrentCulture),
        FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = HubTheme.Faint,
        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 6),
    };

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
        if (count > 1) row.Children.Add(new TextBlock { Text = count.ToString(CultureInfo.CurrentCulture), FontSize = 11, Foreground = HubTheme.Dim, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var who = new List<string>();
        if (message.MyReaction != null) who.Add($"You {message.MyReaction} · click to remove");
        if (message.TheirReaction != null) who.Add($"{_friend?.Name} {message.TheirReaction}");
        var scale = new ScaleTransform(1, 1);
        var pill = new Border
        {
            Background = TheirBubble, BorderBrush = HubTheme.Frozen(Color.FromRgb(0x18, 0x18, 0x1A)),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Padding = new Thickness(5, 1, 6, 2), Child = row,
            HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = message.Mine ? new Thickness(0, -8, 10, 0) : new Thickness(10, -8, 0, 0),
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

    /// <summary>One to three emoji and nothing else: shown big, without a bubble, as messengers do.</summary>
    internal static bool IsBigEmoji(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 40) return false;
        var parts = new List<string>();
        var elements = StringInfo.GetTextElementEnumerator(trimmed);
        while (elements.MoveNext())
        {
            string element = (string)elements.Current;
            if (element.Trim().Length == 0) continue; // spaces between emoji are fine
            parts.Add(element);
        }
        return parts.Count is >= 1 and <= 3
            && parts.All(p => ChatReaction.Clean(p) != null && (char.IsSurrogate(p[0]) || p[0] >= '←'));
    }

    FrameworkElement Bubble(FriendMessage message, bool joinsPrevious, bool joinsNext, bool lastOfMine)
    {
        bool gif = message.Gif != null;
        bool big = !gif && IsBigEmoji(message.Text);
        FrameworkElement content = gif ? GifPlayer.View(message.Gif!, 220, 160, 0, _social.Animate(), maxSide: 320, fitHeight: true)
                                 : big ? new EmojiText { Text = message.Text.Trim(), FontSize = 40 }
                                 : new EmojiText { Text = message.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = Brushes.White };

        // Bubbles of one group run together: the corners where they meet are small, like in every messenger.
        const double round = 18, joined = 5;
        var corners = message.Mine
            ? new CornerRadius(round, joinsPrevious ? joined : round, joinsNext ? joined : round, round)
            : new CornerRadius(joinsPrevious ? joined : round, round, round, joinsNext ? joined : round);
        var bubble = new Border
        {
            Background = gif || big ? Brushes.Transparent : message.Mine ? MyBubble : TheirBubble,
            CornerRadius = corners, Padding = gif || big ? new Thickness(0) : new Thickness(13, 8, 13, 9), MaxWidth = 380, Child = content,
            Opacity = message.State == ChatState.Sending ? 0.6 : 1, ClipToBounds = gif,
            ToolTip = DateTimeOffset.FromUnixTimeSeconds(message.Sent).LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
        };
        if (gif)
        {
            // The GIF takes the bubble's shape, joined corners included.
            void ClipGif()
            {
                if (bubble.ActualWidth <= 0) return;
                bubble.Clip = Rounded(new Rect(0, 0, bubble.ActualWidth, bubble.ActualHeight), corners);
            }
            bubble.SizeChanged += (_, _) => ClipGif();
        }
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
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, Background = HubTheme.Card,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Opacity = 0, ToolTip = "React",
            Child = HubTheme.Glyph("", 13, HubTheme.Dim),
        };
        smiley.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ChatExtras.ShowReactionBar(bubble, _social, message.MyReaction, emoji => React(message, emoji));
        };

        var withReactions = new StackPanel();
        withReactions.Children.Add(bubble);
        var pill = Reactions(message);
        if (pill != null) withReactions.Children.Add(pill);

        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, Background = Brushes.Transparent };
        if (message.Mine)
        {
            line.Children.Add(smiley);
            line.Children.Add(withReactions);
        }
        else
        {
            // Their face sits beside the last bubble of their group; the others keep its room.
            var face = joinsNext || _friend == null ? (FrameworkElement)new Border { Width = 28 } : SmallFace(_friend, 28);
            face.VerticalAlignment = VerticalAlignment.Bottom;
            face.Margin = new Thickness(0, 0, 8, pill != null ? 14 : 0); // level with the bubble, not its reactions
            line.Children.Add(face);
            line.Children.Add(withReactions);
            line.Children.Add(smiley);
        }
        line.MouseEnter += (_, _) => smiley.Opacity = 1;
        line.MouseLeave += (_, _) => smiley.Opacity = 0;

        var stack = new StackPanel
        {
            HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(0, joinsPrevious ? 2 : 8, 0, 0),
        };
        stack.Children.Add(line);
        if (message.State == ChatState.Failed)
        {
            var retry = new TextBlock
            {
                Text = "Not sent · Click to try again", FontSize = 11, Foreground = HubTheme.Red, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 6, 0),
            };
            retry.MouseLeftButtonUp += (_, _) => { if (_friend != null) _social.Retry(_friend, message); };
            stack.Children.Add(retry);
        }
        else if (message.State == ChatState.Sending || lastOfMine)
        {
            stack.Children.Add(new TextBlock
            {
                Text = message.State == ChatState.Sending ? "Sending…" : "Sent", FontSize = 10.5, Foreground = HubTheme.Faint,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 6, 0),
            });
        }
        return stack;
    }

    /// <summary>A rectangle with its own radius at each corner (WPF's rounded rectangle has one for all).</summary>
    static Geometry Rounded(Rect r, CornerRadius c)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(r.Left + c.TopLeft, r.Top), true, true);
            g.LineTo(new Point(r.Right - c.TopRight, r.Top), false, false);
            g.ArcTo(new Point(r.Right, r.Top + c.TopRight), new Size(c.TopRight, c.TopRight), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(r.Right, r.Bottom - c.BottomRight), false, false);
            g.ArcTo(new Point(r.Right - c.BottomRight, r.Bottom), new Size(c.BottomRight, c.BottomRight), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(r.Left + c.BottomLeft, r.Bottom), false, false);
            g.ArcTo(new Point(r.Left, r.Bottom - c.BottomLeft), new Size(c.BottomLeft, c.BottomLeft), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(r.Left, r.Top + c.TopLeft), false, false);
            g.ArcTo(new Point(r.Left + c.TopLeft, r.Top), new Size(c.TopLeft, c.TopLeft), 0, false, SweepDirection.Clockwise, false, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // ───────────────────────── small pieces ─────────────────────────

    /// <summary>A round button for the header: an icon, or an emoji in color.</summary>
    static Border IconButton(string glyph, string tip, bool emoji = false)
    {
        var button = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            Margin = new Thickness(4, 0, 0, 0), ToolTip = tip,
            Child = emoji ? ChatExtras.Emoji(glyph, 15) : HubTheme.Glyph(glyph, 15),
        };
        button.MouseEnter += (_, _) => button.Background = HubTheme.CardStrong;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        return button;
    }

    /// <summary>A small rounded label that works as a button ("GIF", "Say hi").</summary>
    static Border Chip(string text, string tip)
    {
        var chip = new Border
        {
            Background = HubTheme.CardStrong, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Cursor = Cursors.Hand, ToolTip = tip,
            Child = new EmojiText { Text = text, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
        };
        chip.MouseEnter += (_, _) => chip.Background = HubTheme.Selected;
        chip.MouseLeave += (_, _) => chip.Background = HubTheme.CardStrong;
        return chip;
    }

    FrameworkElement SmallFace(Friend friend, double size)
    {
        var (text, fill) = _social.Face(friend.Name);
        return HubTheme.Face(text, fill, size);
    }

    FrameworkElement FaceWithDot(Friend friend, double size)
    {
        var (text, fill) = _social.Face(friend.Name);
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(HubTheme.Face(text, fill, size));
        if (_social.IsOnline(friend))
            grid.Children.Add(new Border
            {
                Width = size * 0.3, Height = size * 0.3, CornerRadius = new CornerRadius(size * 0.15), Background = HubTheme.Green,
                BorderBrush = DotEdge, BorderThickness = new Thickness(Math.Max(2, size / 22)),
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
