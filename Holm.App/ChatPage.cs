using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// Chat, as in the messengers people know (the WPF app's ChatPage): the conversations on the left (searchable, newest
/// first), the one you're in on the right with bubbles that run together, big emoji, reactions, and a round message box.
/// </summary>
internal sealed class ChatPage : HubView
{
    static IBrush B(uint argb) => new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();

    // The page's own shades: the list a little lighter than the conversation, bubbles that stand out from both.
    static readonly IBrush ListBack = B(0x0EFFFFFF), ConversationBack = B(0x40000000), TheirBubble = B(0xFF2E2E31),
        Composer = B(0xFF2A2A2D), ComposerEdge = B(0x1CFFFFFF), DotEdge = B(0xFF222224);
    static readonly IBrush MyBubble = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(0x2E, 0x7B, 0xFF), 0), new GradientStop(Color.FromRgb(0x7B, 0x4D, 0xFF), 1) },
    }.ToImmutable();

    /// <summary>Messages this close together from the same side run together in one group.</summary>
    const long GroupSeconds = 180;
    /// <summary>After a pause this long the time shows again between the messages.</summary>
    const long TimeGapSeconds = 15 * 60;
    /// <summary>How many of the latest messages show; "Show earlier messages" adds more.</summary>
    internal const int ShownAtFirst = 80;

    readonly SocialContext _social;
    readonly StackPanel _list = new(), _messages = new();
    readonly ScrollViewer _scroll = null!;
    readonly Border _header = new();
    readonly TextBox _input = null!, _search = null!;
    readonly TextBlock _counter = null!;
    readonly Border _send = null!;
    Friend? _friend;
    int _shown = ShownAtFirst;
    HashSet<string>? _changed;

    public ChatPage(SocialContext social, Friend? with)
    {
        _social = social;
        var friends = social.Friends();
        if (friends.Count == 0)
        {
            Child = AddFriendPage.EmptyWithAdd(social, "", "No friends yet", "Send a friend your invite code, or type in theirs. Then chat here.");
            return;
        }

        // One card, split in two: the conversations and the one you're in.
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("272,1,*") };

        // ── Left: search and conversations ──
        var left = new Grid { Background = ListBack, RowDefinitions = new RowDefinitions("Auto,*") };
        var searchHost = new Grid { Margin = new Thickness(12, 12, 52, 8) };
        _search = HubTheme.Field("Search");
        _search.Padding = new Thickness(30, 6, 6, 6);
        _search.Background = B(0x14FFFFFF);
        _search.BorderBrush = Brushes.Transparent;
        searchHost.Children.Add(_search);
        var magnifier = HubTheme.Glyph("", 12, HubTheme.Faint);
        magnifier.HorizontalAlignment = HorizontalAlignment.Left;
        magnifier.Margin = new Thickness(12, 0, 0, 0);
        magnifier.IsHitTestVisible = false;
        searchHost.Children.Add(magnifier);
        _search.TextChanged += (_, _) => ShowList();
        left.Children.Add(searchHost);
        var addFriend = RoundButton(HubTheme.Glyph("", 14), "Add a friend", 34, HubTheme.Card, HubTheme.Selected);
        addFriend.HorizontalAlignment = HorizontalAlignment.Right;
        addFriend.VerticalAlignment = VerticalAlignment.Top;
        addFriend.Margin = new Thickness(0, 14, 12, 0);
        addFriend.PointerReleased += (_, e) => { e.Handled = true; social.Open?.Invoke("add", null); };
        left.Children.Add(addFriend);
        _list.Margin = new Thickness(8, 0, 2, 8);
        var listScroll = HubTheme.Scroller(_list);
        Grid.SetRow(listScroll, 1);
        left.Children.Add(listScroll);
        layout.Children.Add(left);

        var divider = new Border { Background = HubTheme.Line };
        Grid.SetColumn(divider, 1);
        layout.Children.Add(divider);

        // ── Right: header, messages, message box ──
        var conversation = new Grid { Background = ConversationBack, RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        _header.Padding = new Thickness(16, 10, 12, 10);
        _header.BorderBrush = HubTheme.Line;
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        _header.Background = ListBack;
        conversation.Children.Add(_header);
        _messages.Margin = new Thickness(14, 6, 12, 10);
        _scroll = HubTheme.Scroller(_messages);
        Grid.SetRow(_scroll, 1);
        conversation.Children.Add(_scroll);

        // The message box: attachments on the left, send on the right.
        var box = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var attach = RoundButton(HubTheme.Glyph("", 14, HubTheme.Dim), "Send files", 30, Brushes.Transparent, HubTheme.Hover);
        attach.Margin = new Thickness(5, 0, 0, 0);
        attach.VerticalAlignment = VerticalAlignment.Center;
        attach.PointerReleased += async (_, e) =>
        {
            e.Handled = true;
            if (_friend is not { } to || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose files to send", AllowMultiple = true });
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths.Length > 0) _social.SendFiles(to, paths);
            _input.Focus();
        };
        box.Children.Add(attach);
        _input = HubTheme.Field("Message…", multiline: true);
        _input.Background = Brushes.Transparent;
        _input.BorderThickness = new Thickness(0);
        foreach (string state in new[] { "PointerOver", "Focused" }) _input.Resources["TextControlBackground" + state] = Brushes.Transparent;
        _input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        _input.Padding = new Thickness(4, 9, 4, 9);
        _input.FontSize = 13.5;
        _input.MaxHeight = 120;
        _input.AcceptsReturn = false;
        _input.KeyDown += (_, e) =>
        {
            // Enter sends, Shift+Enter starts a new line.
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                int caret = _input.CaretIndex;
                _input.Text = (_input.Text ?? "").Insert(caret, "\n");
                _input.CaretIndex = caret + 1;
                return;
            }
            SendInput();
        };
        var inputHost = new Grid();
        inputHost.Children.Add(_input);
        _counter = new TextBlock { FontSize = 10.5, Foreground = HubTheme.Faint, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -14, 6, 0), IsHitTestVisible = false };
        inputHost.Children.Add(_counter);
        Grid.SetColumn(inputHost, 1);
        box.Children.Add(inputHost);
        _send = RoundButton(HubTheme.Glyph("", 13), "Send (Enter)", 32, HubTheme.Accent, HubTheme.Accent);
        _send.Margin = new Thickness(4, 4, 5, 4);
        _send.VerticalAlignment = VerticalAlignment.Bottom;
        _send.PointerReleased += (_, e) => { e.Handled = true; SendInput(); };
        Grid.SetColumn(_send, 2);
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

        RefreshComposer();
        // Unread first, then whoever wrote last.
        Select(with ?? friends.OrderByDescending(f => social.Chats.Unread(f.Key)).ThenByDescending(f => social.Chats.Last(f.Key)?.Sent ?? 0).First());
        social.Chats.Changed += OnChanged;
        social.Changed += OnFriendsChanged;
        AttachedToVisualTree += (_, _) => _input.Focus();
    }

    /// <summary>The friend whose conversation is shown.</summary>
    public Friend? Friend => _friend;

    public override void Detach()
    {
        _social.Chats.Changed -= OnChanged;
        _social.Changed -= OnFriendsChanged;
    }

    /// <summary>Someone came online or changed their picture or status.</summary>
    void OnFriendsChanged() => Dispatcher.UIThread.Post(() => { ShowHeader(); ShowList(); });

    /// <summary>A conversation changed: the page catches up once, after input and drawing.</summary>
    void OnChanged(string key)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_changed != null) { _changed.Add(key); return; }
            _changed = new HashSet<string> { key };
            Dispatcher.UIThread.Post(CatchUp, DispatcherPriority.Background);
        });
    }

    void CatchUp()
    {
        var changed = _changed;
        _changed = null;
        if (changed == null || Parent == null) return; // the page was left meanwhile
        if (_friend != null && changed.Contains(_friend.Key))
        {
            // A reaction to an older message mustn't jump to the end; a new message at the end does.
            bool atEnd = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 24;
            var offset = _scroll.Offset;
            ShowMessages(scrollToEnd: atEnd);
            if (!atEnd) _scroll.Offset = offset;
            if (TopLevel.GetTopLevel(this) is Window { IsActive: true }) _social.Chats.MarkRead(_friend.Key);
        }
        ShowList();
    }

    public void Select(Friend friend)
    {
        if (_friend?.Key != friend.Key) _shown = ShownAtFirst;
        _friend = _social.Friends().FirstOrDefault(f => f.Key == friend.Key) ?? friend;
        ShowHeader();
        ShowMessages();
        _social.Chats.MarkRead(_friend.Key); // the page is open on them
        ShowList();
        _input.Focus();
    }

    void SendInput()
    {
        if (_friend == null || (_input.Text ?? "").Trim().Length == 0) return;
        _social.Send(_friend, _input.Text!);
        _input.Text = "";
    }

    /// <summary>The send button lights up when there's something to send; a long text says how many messages it makes.</summary>
    void RefreshComposer()
    {
        string text = _input.Text ?? "";
        bool any = text.Trim().Length > 0;
        _send.Opacity = any ? 1 : 0.35;
        int parts = any ? ChatText.Split(text).Count : 0;
        _counter.Text = parts > 1 ? $"{parts} messages" : "";
    }

    // ───────────── the conversations ─────────────

    void ShowList()
    {
        _list.Children.Clear();
        string filter = (_search.Text ?? "").Trim();
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

    Control Row(Friend friend)
    {
        int unread = _social.Chats.Unread(friend.Key);
        var last = _social.Chats.Last(friend.Key);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(FaceWithDot(friend, 44));

        var words = new Grid { Margin = new Thickness(11, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        words.Children.Add(new TextBlock { Text = friend.Name, FontSize = 13.5, FontWeight = unread > 0 ? FontWeight.Bold : FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (last != null)
        {
            var when = new TextBlock
            {
                Text = ListTime(DateTimeOffset.FromUnixTimeSeconds(last.Sent).LocalDateTime), FontSize = 11, Margin = new Thickness(8, 1, 0, 0),
                Foreground = unread > 0 ? HubTheme.Accent : HubTheme.Faint, FontWeight = unread > 0 ? FontWeight.SemiBold : FontWeight.Normal,
            };
            Grid.SetColumn(when, 1);
            words.Children.Add(when);
        }
        string text = last == null ? friend.Status ?? (_social.IsOnline(friend) ? "Active now" : "Say hi 👋")
                    : (last.Mine ? "You: " : "") + (last.Gif != null ? "GIF" : last.Text.Replace('\n', ' '));
        var preview = new TextBlock
        {
            Text = text, FontSize = 12, Foreground = unread > 0 ? Brushes.White : HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = unread > 0 ? FontWeight.SemiBold : FontWeight.Normal, Margin = new Thickness(0, 3, 0, 0),
        };
        Grid.SetRow(preview, 1);
        words.Children.Add(preview);
        if (unread > 0)
        {
            var badge = new Border
            {
                Background = HubTheme.Accent, CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(8, 3, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = unread > 99 ? "99+" : unread.ToString(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
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
            Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(10), Cursor = new Cursor(StandardCursorType.Hand), Child = row,
            Background = selected ? HubTheme.Selected : Brushes.Transparent, Margin = new Thickness(0, 0, 0, 1),
        };
        item.PointerEntered += (_, _) => { if (!selected) item.Background = HubTheme.Hover; };
        item.PointerExited += (_, _) => { if (!selected) item.Background = Brushes.Transparent; };
        item.PointerReleased += (_, _) => Select(friend);
        return item;
    }

    /// <summary>When the last message came, as short as the list has room for: 17:42, Mon, 3 Nov.</summary>
    static string ListTime(DateTime at)
        => at.Date == DateTime.Today ? at.ToString("t", CultureInfo.CurrentCulture)
         : at.Date > DateTime.Today.AddDays(-7) ? at.ToString("ddd", CultureInfo.CurrentCulture)
         : at.ToString("d MMM", CultureInfo.CurrentCulture);

    // ───────────── the one you're in ─────────────

    void ShowHeader()
    {
        if (_friend == null) return;
        var friend = _friend;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(FaceWithDot(friend, 38));
        var words = new StackPanel { Margin = new Thickness(11, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = friend.Name, FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        bool online = _social.IsOnline(friend);
        string line = online ? "Active now" : "Offline · messages wait about 12 hours";
        if (friend.Status != null) line += " · " + friend.Status;
        words.Children.Add(new TextBlock { Text = line, FontSize = 11.5, Foreground = online ? HubTheme.Green : HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var wave = RoundButton(new TextBlock { Text = "\U0001F44B", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                               $"Wave at {friend.Name}", 34, Brushes.Transparent, HubTheme.CardStrong);
        wave.Margin = new Thickness(4, 0, 0, 0);
        wave.PointerReleased += (_, e) => { e.Handled = true; _social.Wave(friend); };
        actions.Children.Add(wave);
        if (_social.Stream is { } stream)
        {
            var share = RoundButton(HubTheme.Glyph("\uE7F4", 14), $"Share your screen with {friend.Name}", 34, Brushes.Transparent, HubTheme.CardStrong);
            share.Margin = new Thickness(4, 0, 0, 0);
            share.PointerReleased += (_, e) => { e.Handled = true; stream(friend); };
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
        int first = Math.Max(0, messages.Count - _shown);
        if (first > 0)
        {
            var earlier = Chip("Show earlier messages", $"{first} more");
            earlier.HorizontalAlignment = HorizontalAlignment.Center;
            earlier.Margin = new Thickness(0, 14, 0, 4);
            earlier.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                // Keep the message you were looking at where it is while the earlier ones go in above it.
                double fromEnd = _scroll.Extent.Height - _scroll.Offset.Y;
                _shown += ShownAtFirst * 2;
                ShowMessages(scrollToEnd: false);
                _scroll.UpdateLayout();
                _scroll.Offset = new Vector(0, _scroll.Extent.Height - fromEnd);
            };
            _messages.Children.Add(earlier);
        }
        // The face and the friend's name at the top, as messengers start a conversation.
        else _messages.Children.Add(Welcome(_friend, small: true));
        for (int i = first; i < messages.Count; i++)
        {
            var message = messages[i];
            var previous = i > 0 ? messages[i - 1] : null;
            var next = i + 1 < messages.Count ? messages[i + 1] : null;
            bool newTime = previous == null || message.Sent - previous.Sent >= TimeGapSeconds
                           || DateTimeOffset.FromUnixTimeSeconds(previous.Sent).LocalDateTime.Date != DateTimeOffset.FromUnixTimeSeconds(message.Sent).LocalDateTime.Date;
            if (newTime) _messages.Children.Add(TimeLabel(DateTimeOffset.FromUnixTimeSeconds(message.Sent).LocalDateTime));
            bool joinsPrevious = !newTime && Together(previous, message);
            bool joinsNext = next != null && Together(message, next) && next.Sent - message.Sent < TimeGapSeconds;
            _messages.Children.Add(Bubble(message, joinsPrevious, joinsNext, message.Mine && next == null));
        }
        if (scrollToEnd) Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }

    static bool Together(FriendMessage? a, FriendMessage? b)
        => a != null && b != null && a.Mine == b.Mine && b.Sent - a.Sent < GroupSeconds
           && a.MyReaction == null && a.TheirReaction == null; // a reaction pill needs room under its bubble

    /// <summary>The top of a conversation: their face, name and a word on how private it is (or a way to start).</summary>
    Control Welcome(Friend friend, bool small = false)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, small ? 18 : 40, 20, small ? 10 : 0) };
        var face = FaceWithDot(friend, small ? 56 : 84);
        face.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(face);
        stack.Children.Add(new TextBlock { Text = friend.Name, FontSize = small ? 15 : 18, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
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
            hi.PointerReleased += (_, e) => { e.Handled = true; _social.Send(friend, "👋"); };
            stack.Children.Add(hi);
        }
        return stack;
    }

    static TextBlock TimeLabel(DateTime at) => new()
    {
        Text = (at.Date == DateTime.Today ? "Today" : at.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : at.ToString("ddd d MMM", CultureInfo.CurrentCulture))
               + " " + at.ToString("t", CultureInfo.CurrentCulture),
        FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = HubTheme.Faint,
        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 6),
    };

    void React(FriendMessage message, string? emoji)
    {
        if (_friend != null) _social.React(_friend, message, emoji);
    }

    /// <summary>The reactions under a bubble: a small pill over its lower edge with both sides' emoji. A click takes ours away.</summary>
    Control? Reactions(FriendMessage message)
    {
        var emoji = new[] { message.TheirReaction, message.MyReaction }.Where(e => e != null).Distinct().ToList();
        if (emoji.Count == 0) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string e in emoji!) row.Children.Add(new TextBlock { Text = e, FontSize = 13 });
        int count = (message.TheirReaction != null ? 1 : 0) + (message.MyReaction != null ? 1 : 0);
        if (count > 1) row.Children.Add(new TextBlock { Text = count.ToString(CultureInfo.CurrentCulture), FontSize = 11, Foreground = HubTheme.Dim, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var who = new List<string>();
        if (message.MyReaction != null) who.Add($"You {message.MyReaction} · click to remove");
        if (message.TheirReaction != null) who.Add($"{_friend?.Name} {message.TheirReaction}");
        var pill = new Border
        {
            Background = TheirBubble, BorderBrush = B(0xFF18181A), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11),
            Padding = new Thickness(5, 1, 6, 2), Child = row,
            HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = message.Mine ? new Thickness(0, -8, 10, 0) : new Thickness(10, -8, 0, 0),
            Cursor = new Cursor(message.MyReaction != null ? StandardCursorType.Hand : StandardCursorType.Arrow),
        };
        ToolTip.SetTip(pill, string.Join("\n", who));
        pill.PointerReleased += (_, e) =>
        {
            if (message.MyReaction == null) return;
            e.Handled = true;
            React(message, null);
        };
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

    Control Bubble(FriendMessage message, bool joinsPrevious, bool joinsNext, bool lastOfMine)
    {
        bool gif = message.Gif != null;
        bool big = !gif && IsBigEmoji(message.Text);
        Control content = gif
            ? new TextBlock { Text = "GIF · " + message.Gif, FontSize = 12, Foreground = HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 }
            : big ? new TextBlock { Text = message.Text.Trim(), FontSize = 40 }
            : new SelectableTextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = Brushes.White };

        // Bubbles of one group run together: the corners where they meet are small, like in every messenger.
        const double round = 18, joined = 5;
        var corners = message.Mine
            ? new CornerRadius(round, joinsPrevious ? joined : round, joinsNext ? joined : round, round)
            : new CornerRadius(joinsPrevious ? joined : round, round, round, joinsNext ? joined : round);
        var bubble = new Border
        {
            Background = big ? Brushes.Transparent : message.Mine ? MyBubble : TheirBubble,
            CornerRadius = corners, Padding = big ? new Thickness(0) : new Thickness(13, 8, 13, 9), MaxWidth = 380, Child = content,
            Opacity = message.State == ChatState.Sending ? 0.6 : 1,
        };
        ToolTip.SetTip(bubble, DateTimeOffset.FromUnixTimeSeconds(message.Sent).LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
        // A double click gives a heart (or takes it away), like a double tap on Instagram.
        bubble.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            React(message, message.MyReaction == ChatReaction.DoubleClick ? null : ChatReaction.DoubleClick);
        };
        // Copy, as from any chat, and React.
        var copy = new MenuItem { Header = gif ? "Copy GIF link" : "Copy" };
        copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) try { await clipboard.SetTextAsync(message.Gif ?? message.Text); } catch { } };
        var reactItem = new MenuItem { Header = "React…" };
        reactItem.Click += (_, _) => ShowReactionBar(bubble, message);
        bubble.ContextMenu = new ContextMenu { Items = { reactItem, copy } };

        // The smiley beside the bubble on hover opens the reaction bar.
        var smiley = RoundButton(HubTheme.Glyph("", 13, HubTheme.Dim), "React", 26, HubTheme.Card, HubTheme.CardStrong);
        smiley.VerticalAlignment = VerticalAlignment.Center;
        smiley.Margin = new Thickness(6, 0, 6, 0);
        smiley.Opacity = 0;
        smiley.PointerReleased += (_, e) => { e.Handled = true; ShowReactionBar(bubble, message); };

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
            Control face = joinsNext || _friend == null ? new Border { Width = 28 } : SmallFace(_friend, 28);
            face.VerticalAlignment = VerticalAlignment.Bottom;
            face.Margin = new Thickness(0, 0, 8, pill != null ? 14 : 0);
            line.Children.Add(face);
            line.Children.Add(withReactions);
            line.Children.Add(smiley);
        }
        line.PointerEntered += (_, _) => smiley.Opacity = 1;
        line.PointerExited += (_, _) => smiley.Opacity = 0;

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
                Text = "Not sent · Click to try again", FontSize = 11, Foreground = HubTheme.Red, Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 6, 0),
            };
            retry.PointerReleased += (_, _) => { if (_friend != null) _social.Retry(_friend, message); };
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

    /// <summary>The reaction bar over a message: the quick reactions, the one already given marked (a click takes it away).</summary>
    void ShowReactionBar(Control target, FriendMessage message)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        var flyout = new Flyout { Placement = PlacementMode.Top, ShowMode = FlyoutShowMode.Transient };
        foreach (string emoji in _social.QuickReactions())
        {
            string e = emoji;
            bool mine = e == message.MyReaction;
            var button = RoundButton(new TextBlock { Text = e, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                                     e, 40, mine ? HubTheme.Selected : Brushes.Transparent, HubTheme.CardStrong);
            button.PointerReleased += (_, ev) => { ev.Handled = true; flyout.Hide(); React(message, mine ? null : e); };
            row.Children.Add(button);
        }
        flyout.Content = new Border { Background = HubTheme.Panel, BorderBrush = HubTheme.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(26), Child = row };
        flyout.FlyoutPresenterClasses.Add("bare");
        flyout.ShowAt(target);
    }

    // ───────────── small pieces ─────────────

    static Border RoundButton(Control content, string tip, double size, IBrush rest, IBrush hover)
    {
        var button = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Cursor = new Cursor(StandardCursorType.Hand),
            Background = rest, Child = content,
        };
        ToolTip.SetTip(button, tip);
        button.PointerEntered += (_, _) => button.Background = hover;
        button.PointerExited += (_, _) => button.Background = rest;
        return button;
    }

    /// <summary>A small rounded label that works as a button ("Say hi").</summary>
    static Border Chip(string text, string tip)
    {
        var chip = new Border
        {
            Background = HubTheme.CardStrong, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
        };
        ToolTip.SetTip(chip, tip);
        chip.PointerEntered += (_, _) => chip.Background = HubTheme.Selected;
        chip.PointerExited += (_, _) => chip.Background = HubTheme.CardStrong;
        return chip;
    }

    Control SmallFace(Friend friend, double size)
    {
        var (text, fill) = _social.Face(friend.Name);
        return HubTheme.Face(text, fill, size);
    }

    Control FaceWithDot(Friend friend, double size)
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
    public static StackPanel Empty(string glyph, string title, string text)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380 };
        stack.Children.Add(new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = HubTheme.Card, Child = HubTheme.Glyph(glyph, 26, HubTheme.Dim),
            Margin = new Thickness(0, 0, 0, 16),
        });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = text, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
        return stack;
    }
}
