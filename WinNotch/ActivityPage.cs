using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Active now: what your friends are doing (music, a call, a game, a timer, a stream), who's online, and what they
/// see of you. Friends are asked when the page opens and every few minutes while it stays open.
/// </summary>
internal sealed class ActivityPage : HubView
{
    static readonly TimeSpan AskEvery = TimeSpan.FromMinutes(3);

    readonly SocialContext _social;
    readonly StackPanel _content = new();
    readonly DispatcherTimer _tick;
    DateTime _asked;

    public ActivityPage(SocialContext social)
    {
        _social = social;
        Child = HubTheme.Scroller(_content);
        social.Changed += Show;
        // Once a minute: the "for 12 min" times move on, and every few minutes friends are asked again.
        _tick = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        Ask();
        Show();
    }

    public override void Detach()
    {
        _social.Changed -= Show;
        _tick.Stop();
    }

    void Tick()
    {
        if (DateTime.UtcNow - _asked >= AskEvery) Ask();
        Show();
    }

    void Ask()
    {
        _asked = DateTime.UtcNow;
        _social.Ping();
    }

    void Show()
    {
        _content.Children.Clear();
        _content.Children.Add(YouCard());

        var friends = _social.Friends();
        if (friends.Count == 0)
        {
            _content.Children.Add(new Border { Height = 40 });
            _content.Children.Add(AddFriendPage.EmptyWithAdd(_social, "", "No friends yet",
                "Send a friend your invite code, or type in theirs. Once you're friends, you'll see what they're up to here."));
            return;
        }

        var active = friends.Where(f => _social.ActivityOf(f) != null).OrderBy(f => f.Name).ToList();
        var online = friends.Where(f => _social.ActivityOf(f) == null && _social.IsOnline(f)).OrderBy(f => f.Name).ToList();
        var offline = friends.Where(f => !_social.IsOnline(f)).OrderBy(f => f.Name).ToList();

        if (active.Count > 0)
        {
            _content.Children.Add(Section($"Active now — {active.Count}"));
            var cards = new WrapPanel();
            foreach (var friend in active) cards.Children.Add(ActiveCard(friend, _social.ActivityOf(friend)!));
            _content.Children.Add(cards);
        }
        if (online.Count > 0)
        {
            _content.Children.Add(Section($"Online — {online.Count}"));
            _content.Children.Add(List(online, dim: false));
        }
        if (offline.Count > 0)
        {
            _content.Children.Add(Section($"Offline — {offline.Count}"));
            _content.Children.Add(List(offline, dim: true));
        }
        if (active.Count == 0 && online.Count == 0)
            _content.Children.Add(new TextBlock
            {
                Text = "Friends show up here as soon as their Holm answers. Holm asks every few minutes while this page is open.",
                Foreground = HubTheme.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 0, 0),
            });
    }

    static TextBlock Section(string title)
    {
        var caption = HubTheme.Caption(title);
        caption.Margin = new Thickness(4, 20, 0, 8);
        return caption;
    }

    /// <summary>What friends see of you right now, and whether you share it at all.</summary>
    FrameworkElement YouCard()
    {
        var mine = _social.Mine();
        string title, detail;
        Color color;
        string glyph;
        if (!_social.Sharing())
        {
            (title, detail, glyph, color) = ("You don't share your activity", "Turn on Settings › Friends › Share my activity to show friends what you're up to.", "", Color.FromRgb(0x63, 0x63, 0x66));
        }
        else if (mine == null)
        {
            (title, detail, glyph, color) = ("Friends see you as online", "When you play music, a game, start a call, a timer or a stream, they'll see it here.", "", Color.FromRgb(0x30, 0xD1, 0x58));
        }
        else
        {
            (glyph, color) = Look(mine.Kind);
            title = "Friends see: " + Describe(mine);
            detail = mine.Detail ?? "";
        }
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = HubTheme.IconTile(glyph, color, 40);
        tile.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(tile);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Length > 0) words.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14), Child = row };
    }

    FrameworkElement ActiveCard(Friend friend, FriendActivity activity)
    {
        var (glyph, color) = Look(activity.Kind);
        var stack = new StackPanel();

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.Children.Add(Face(friend, 44));
        var who = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(NameLine(friend, 15));
        who.Children.Add(new TextBlock { Text = friend.Status ?? "Online", FontSize = 12, Foreground = HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(who, 1);
        top.Children.Add(who);
        stack.Children.Add(top);

        // The activity, in a strip of its own color.
        var strip = new Grid();
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strip.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = HubTheme.IconTile(glyph, color, 36);
        tile.Margin = new Thickness(0, 0, 12, 0);
        strip.Children.Add(tile);
        var what = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        what.Children.Add(new TextBlock { Text = Verb(activity.Kind).ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = HubTheme.Frozen(color) });
        what.Children.Add(new TextBlock { Text = activity.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string detail = string.Join(" · ", new[] { activity.Detail, For(activity.Since) }.Where(s => !string.IsNullOrEmpty(s)));
        if (detail.Length > 0) what.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(what, 1);
        strip.Children.Add(what);
        stack.Children.Add(new Border
        {
            Background = HubTheme.Frozen(Color.FromArgb(0x24, color.R, color.G, color.B)), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10), Margin = new Thickness(0, 12, 0, 12), Child = strip,
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var message = HubTheme.Action("Message");
        message.Click += (_, _) => _social.Open?.Invoke("chat", friend);
        var wave = HubTheme.Action("Wave");
        wave.Margin = new Thickness(8, 0, 0, 0);
        wave.Click += (_, _) => _social.Wave(friend);
        actions.Children.Add(message);
        actions.Children.Add(wave);
        if (_social.Stream != null && activity.Kind != FriendActivity.Stream)
        {
            var stream = HubTheme.Action("Stream to");
            stream.Margin = new Thickness(8, 0, 0, 0);
            stream.ToolTip = $"Invite {friend.Name} to watch your screen";
            stream.Click += (_, _) => _social.Stream(friend);
            actions.Children.Add(stream);
        }
        stack.Children.Add(actions);

        return new Border
        {
            Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Width = 300,
            Margin = new Thickness(0, 0, 12, 12), Child = stack,
        };
    }

    FrameworkElement List(IReadOnlyList<Friend> friends, bool dim)
    {
        var rows = new StackPanel();
        for (int i = 0; i < friends.Count; i++)
        {
            var friend = friends[i];
            if (i > 0) rows.Children.Add(new Border { Height = 1, Background = HubTheme.Line, Margin = new Thickness(62, 0, 0, 0) });
            var row = new Grid { Margin = new Thickness(14, 10, 12, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var face = Face(friend, 36);
            face.Opacity = dim ? 0.55 : 1;
            row.Children.Add(face);
            var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = dim ? 0.6 : 1 };
            words.Children.Add(NameLine(friend, 13));
            words.Children.Add(new TextBlock { Text = friend.Status ?? (dim ? "Offline" : "Online"), FontSize = 12, Foreground = HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var message = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = HubTheme.CardStrong, Cursor = Cursors.Hand,
                Child = HubTheme.Glyph("", 14), ToolTip = "Message " + friend.Name, VerticalAlignment = VerticalAlignment.Center,
            };
            message.MouseLeftButtonUp += (_, _) => _social.Open?.Invoke("chat", friend);
            Grid.SetColumn(message, 2);
            row.Children.Add(message);
            rows.Children.Add(row);
        }
        return new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Child = rows };
    }

    /// <summary>The name, and how many achievements they have if their notch said.</summary>
    FrameworkElement NameLine(Friend friend, double size)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = friend.Name, FontSize = size, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
        if (_social.TrophiesOf(friend) is int trophies and > 0)
        {
            var pill = new StackPanel { Orientation = Orientation.Horizontal };
            pill.Children.Add(HubTheme.Glyph("", 10, HubTheme.Gold));
            pill.Children.Add(new TextBlock { Text = trophies.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = HubTheme.Gold, Margin = new Thickness(4, 0, 0, 0) });
            line.Children.Add(new Border
            {
                Background = HubTheme.Frozen(Color.FromArgb(0x26, 0xFF, 0xC4, 0x3D)), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 7, 1),
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = pill,
                ToolTip = $"{friend.Name} has {trophies} achievements",
            });
        }
        return line;
    }

    FrameworkElement Face(Friend friend, double size)
    {
        var (text, fill) = _social.Face(friend.Name);
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
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

    internal static (string Glyph, Color Color) Look(string kind) => kind switch
    {
        FriendActivity.Music => ("", Color.FromRgb(0x1D, 0xB9, 0x54)),
        FriendActivity.Call => ("", Color.FromRgb(0x30, 0xD1, 0x58)),
        FriendActivity.Game => ("", Color.FromRgb(0x5E, 0x5C, 0xE6)),
        FriendActivity.Timer => ("", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        FriendActivity.Stream => ("", Color.FromRgb(0xFF, 0x37, 0x5F)),
        _ => ("", Color.FromRgb(0x63, 0x63, 0x66)),
    };

    internal static string Verb(string kind) => kind switch
    {
        FriendActivity.Music => "Listening to",
        FriendActivity.Call => "In a call",
        FriendActivity.Game => "Playing",
        FriendActivity.Timer => "On a timer",
        FriendActivity.Stream => "Live",
        _ => "Online",
    };

    static string Describe(FriendActivity activity) => activity.Kind switch
    {
        FriendActivity.Music => "Listening to " + activity.Title,
        FriendActivity.Game => "Playing " + activity.Title,
        _ => activity.Title,
    };

    /// <summary>"for 12 min", "for 2 h"; empty for a time that isn't known or is in the future.</summary>
    internal static string For(long since)
    {
        if (since <= 0) return "";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(since);
        if (span < TimeSpan.Zero) return "";
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"for {(int)span.TotalMinutes} min";
        return $"for {(int)span.TotalHours} h {span.Minutes} min";
    }
}
