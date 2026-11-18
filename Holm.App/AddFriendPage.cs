using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// Add a friend: your invite code on the left to copy and send, a box for theirs on the right. Whoever types in the
/// other's code becomes their friend at once, nobody has to accept anything. Below, the requests still open.
/// (The WPF app's AddFriendPage.)
/// </summary>
internal sealed class AddFriendPage : HubView
{
    readonly SocialContext _social;
    readonly TextBlock _code, _codeNote, _status;
    readonly TextBox _input;
    readonly Button _copy, _copyMessage, _newCode, _add;
    readonly StackPanel _requests = new();
    readonly Border _requestsCard;
    OwnInvite? _invite;
    bool _adding;

    public AddFriendPage(SocialContext social)
    {
        _social = social;
        var layout = new StackPanel();

        // ── the two halves: your code, their code ──
        var halves = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18,*") };
        var mine = new StackPanel();
        mine.Children.Add(Heading("", HubTheme.Accent, "Your invite code"));
        _code = new TextBlock
        {
            Text = "Making your code…", FontSize = 36, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 18, 0, 6),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), Foreground = Look.Ink,
        };
        // The code shrinks to fit the card rather than run past its edge.
        mine.Children.Add(new Viewbox { Child = _code, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        _codeNote = new TextBlock { Foreground = HubTheme.Dim, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
        mine.Children.Add(_codeNote);
        var buttons = new WrapPanel();
        _copy = HubTheme.Action("Copy code", primary: true);
        _copy.Click += (_, _) => _ = CopyAsync(_invite?.Code, "Code copied");
        _copyMessage = HubTheme.Action("Copy invite message");
        _copy.Margin = _copyMessage.Margin = new Thickness(0, 0, 8, 8);
        _copyMessage.Click += (_, _) => _ = CopyAsync(_invite == null ? null : Message(_invite.Code), "Message copied: paste it into any chat");
        _newCode = HubTheme.Action("New code");
        _newCode.Margin = new Thickness(0, 0, 8, 8);
        ToolTip.SetTip(_newCode, "Makes a new code. The old one still works until it runs out; friends who already used it stay friends");
        _newCode.Click += async (_, _) => await LoadInvite(fresh: true);
        buttons.Children.Add(_copy);
        buttons.Children.Add(_copyMessage);
        buttons.Children.Add(_newCode);
        mine.Children.Add(buttons);
        halves.Children.Add(Card(mine));

        var theirs = new StackPanel();
        theirs.Children.Add(Heading(NotchWindowGlyphs.Friend, HubTheme.Green, "Got a friend's code?"));
        theirs.Children.Add(new TextBlock
        {
            Text = "Type or paste it here. You're friends right away: nobody has to accept anything.",
            Foreground = HubTheme.Dim, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 14),
        });
        var entry = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _input = HubTheme.Field("HOLM-XXXXX-XXXXX");
        _input.FontSize = 16;
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = Add(); } };
        _input.TextChanged += (_, _) =>
        {
            // Codes are upper case; typing them so is a chore.
            string text = _input.Text ?? "";
            if (text != text.ToUpperInvariant()) { int caret = _input.CaretIndex; _input.Text = text.ToUpperInvariant(); _input.CaretIndex = caret; }
            RefreshAdd();
        };
        entry.Children.Add(_input);
        _add = HubTheme.Action("Add friend", primary: true);
        _add.Margin = new Thickness(10, 0, 0, 0);
        _add.Click += (_, _) => _ = Add();
        Grid.SetColumn(_add, 1);
        entry.Children.Add(_add);
        theirs.Children.Add(entry);
        _status = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 0, 0), Foreground = HubTheme.Dim };
        theirs.Children.Add(_status);
        var theirsCard = Card(theirs);
        Grid.SetColumn(theirsCard, 2);
        halves.Children.Add(theirsCard);
        layout.Children.Add(halves);

        // ── requests still open ──
        var requests = new StackPanel();
        requests.Children.Add(HubTheme.Caption("Requests"));
        requests.Children.Add(_requests);
        _requestsCard = new Border { Margin = new Thickness(0, 22, 0, 0), Child = requests };
        layout.Children.Add(_requestsCard);

        // ── friends on an older Holm ──
        var older = HubTheme.Action("Copy my long friend code");
        older.HorizontalAlignment = HorizontalAlignment.Left;
        older.Click += (_, _) => _ = CopyAsync(_social.LongCode(), "Long friend code copied");
        var olderRow = new StackPanel { Margin = new Thickness(2, 26, 0, 0) };
        olderRow.Children.Add(new TextBlock
        {
            Text = "A friend on Holm 1.9.5 or older doesn't know invite codes yet. Send them your long friend code instead; " +
                   "their long code works in the box above too.",
            Foreground = HubTheme.Faint, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });
        olderRow.Children.Add(older);
        layout.Children.Add(olderRow);

        Child = HubTheme.Scroller(layout);
        social.Changed += OnChanged;
        AttachedToVisualTree += async (_, _) => { _input.Focus(); await LoadInvite(fresh: false); };
        RefreshAdd();
        Refresh();
    }

    void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    public override void Detach() => _social.Changed -= OnChanged;

    static string Message(string code) => $"Add me as a friend on Holm: {code} (get Holm at github.com/ademerci/Holm)";

    static Border Card(Control content) => new()
    {
        Background = HubTheme.Card, CornerRadius = new CornerRadius(16), Padding = new Thickness(26, 24, 26, 24), Child = content,
    };

    static Control Heading(string glyph, IBrush color, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = color, Child = HubTheme.Glyph(glyph, 16, ReferenceEquals(color, HubTheme.Accent) ? HubTheme.OnAccent : null) });
        row.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        return row;
    }

    async Task LoadInvite(bool fresh)
    {
        _newCode.IsEnabled = _copy.IsEnabled = _copyMessage.IsEnabled = false;
        if (fresh || _invite == null) _code.Text = "Making your code…";
        var (invite, error) = await _social.Invite(fresh);
        _invite = invite ?? _invite;
        if (_invite == null)
        {
            _code.Text = "No code yet";
            _codeNote.Text = error != null ? error + ". Try again in a moment." : "The invite server (ntfy) couldn't be reached. Check your connection and try again.";
            _newCode.Content = "Try again";
            _newCode.IsEnabled = true;
            return;
        }
        _code.Text = _invite.Code;
        var until = DateTimeOffset.FromUnixTimeSeconds(_invite.Expires).LocalDateTime;
        string when = until.Date == DateTime.Today ? until.ToString("t", CultureInfo.CurrentCulture) : "tomorrow, " + until.ToString("t", CultureInfo.CurrentCulture);
        _codeNote.Text = $"Send it to a friend, or read it out. It works for everyone you give it to until {when}. Anyone with this code becomes your friend and can see what you share with friends – only give it to people you trust.";
        _newCode.Content = "New code";
        _newCode.IsEnabled = _copy.IsEnabled = _copyMessage.IsEnabled = true;
    }

    async Task CopyAsync(string? text, string done)
    {
        if (text == null) return;
        if (await _social.Copy(text)) Say(done, HubTheme.Green, onMine: true);
        else Say("The clipboard is busy, try again", HubTheme.Red, onMine: true);
    }

    /// <summary>Feedback for the buttons on the left goes under the code; for adding, under the box.</summary>
    void Say(string text, IBrush color, bool onMine = false)
    {
        if (onMine)
        {
            string? note = _codeNote.Text;
            _codeNote.Text = text;
            _codeNote.Foreground = color;
            DispatcherTimer.RunOnce(() => { _codeNote.Text = note; _codeNote.Foreground = HubTheme.Dim; }, TimeSpan.FromSeconds(3));
            return;
        }
        _status.Text = text;
        _status.Foreground = color;
    }

    void RefreshAdd() => _add.IsEnabled = !_adding && (_input.Text ?? "").Trim().Length >= 10;

    async Task Add()
    {
        string text = (_input.Text ?? "").Trim();
        if (_adding || text.Length < 10) return;
        _adding = true;
        RefreshAdd();
        Say("Adding…", HubTheme.Dim);
        var (ok, message) = await _social.AddByCode(text);
        _adding = false;
        Say(message, ok ? HubTheme.Green : HubTheme.Red);
        if (ok) _input.Text = "";
        RefreshAdd();
        Refresh();
    }

    /// <summary>Requests to answer, and ours still waiting for an answer.</summary>
    void Refresh()
    {
        _requests.Children.Clear();
        foreach (var (card, requestId) in _social.Incoming())
        {
            var accept = HubTheme.Action("Accept", primary: true);
            accept.Click += (_, _) => _social.Answer(card, requestId, true);
            var decline = HubTheme.Action("Decline");
            decline.Margin = new Thickness(8, 0, 0, 0);
            decline.Click += (_, _) => _social.Answer(card, requestId, false);
            _requests.Children.Add(RequestRow(card, "wants to be your friend", accept, decline));
        }
        foreach (var pending in _social.Pending())
        {
            var withdraw = HubTheme.Action("Withdraw");
            withdraw.Click += (_, _) => _social.Withdraw(pending);
            string line = pending.ByInvite ? "joins as soon as their Holm is on" : "still has to accept your request";
            _requests.Children.Add(RequestRow(pending.Card, line, withdraw));
        }
        _requestsCard.IsVisible = _requests.Children.Count > 0;
    }

    Control RequestRow(Friend card, string line, params Button[] buttons)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var (text, fill) = _social.Face(card.Name);
        row.Children.Add(HubTheme.Face(text, fill, 38));
        var words = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = card.Name, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        words.Children.Add(new TextBlock { Text = line, Foreground = HubTheme.Dim, FontSize = 12.5 });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons) actions.Children.Add(b);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = row };
    }

    /// <summary>An empty friends page with a way to add the first friend.</summary>
    public static Control EmptyWithAdd(SocialContext social, string glyph, string title, string text)
    {
        var stack = ChatPage.Empty(glyph, title, text);
        var add = HubTheme.Action("Add a friend", primary: true);
        add.HorizontalAlignment = HorizontalAlignment.Center;
        add.Margin = new Thickness(0, 18, 0, 0);
        add.Click += (_, _) => social.Open?.Invoke("add", null);
        stack.Children.Add(add);
        return stack;
    }
}

/// <summary>Icons the pages share with the notch.</summary>
internal static class NotchWindowGlyphs
{
    public const string Friend = "";
}
