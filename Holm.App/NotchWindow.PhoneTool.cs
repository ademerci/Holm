using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// The phone tool of the open notch: the phone app signed in to the same Holm account, linked without a friend code.
/// It shows whether the phone is there, its battery and the song it plays, what came in on it lately, and sends it
/// files or the copied text. The phone tells all this with "phone" notes in the account's own inbox
/// (FriendNoteKind.Phone); until it has, the page says how the two link.
/// </summary>
public sealed partial class NotchWindow
{
    const string CellPhoneGlyph = "";
    const int PhoneNewsShown = 4;

    readonly StackPanel _phoneToolPage = new() { IsVisible = false };
    NotchButton _phoneToolBtn = null!;
    readonly TextBlock _phoneName = CardTitle(), _phoneLine = CardMore();
    readonly TextBlock _phoneSongText = new() { FontSize = 12, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 8, 0, 0) };
    readonly StackPanel _phoneNewsRows = new() { Spacing = 4 };
    readonly TextBlock _phoneConnectionsText = new() { FontSize = 12, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 4, 0, 0) };
    readonly TextBlock _phoneUnlinkedText = new() { FontSize = 13, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 0) };
    NotchButton _phoneUnlinkedButton = null!;
    Action _phoneUnlinkedAction = () => { };
    IReadOnlyList<string> _myPhoneConnections = Array.Empty<string>();
    readonly StackPanel _phoneLinked = new(), _phoneUnlinked = new();
    readonly TextBlock _phoneNewsTitle = PageTitle("Lately on the phone");

    /// <summary>The phone this PC heard from last, and what it said.</summary>
    OwnDevice? _myPhone;
    DateTime _myPhoneSeen;
    string? _myPhoneBattery, _myPhoneSong;
    bool _myPhoneCharging, _myPhonePlaying;
    readonly StackPanel _phoneMusicRow = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
    readonly Avalonia.Controls.Shapes.Path _phonePlayIcon = IconPath(PlayGeo, 11, 13);
    readonly List<(string Title, string? Detail, DateTime When, string? Key)> _phoneNews = new();

    Control BuildPhoneToolPage()
    {
        _phoneToolPage.Children.Add(PageTitle("Your phone"));

        var card = Card(CellPhoneGlyph, PhoneIndigo, _phoneName, _phoneLine);
        card.Click += ShowPhoneToolPage; // a click asks nothing of the phone; it shows the newest
        _phoneLinked.Children.Add(card);
        _phoneLinked.Children.Add(_phoneSongText);
        // The phone's player, from here: a note to the phone, which tells back what plays.
        NotchButton MusicButton(Control icon, string what, string tip)
        {
            var b = new NotchButton(icon) { Width = 30, Height = 30 };
            ToolTip.SetTip(b, tip);
            b.Click += () =>
            {
                if (_myPhone is not { } phone) return;
                PulseElement(b);
                _friends.ControlPhoneMusic(phone, what);
                if (what == "toggle")
                {
                    _myPhonePlaying = !_myPhonePlaying; // the phone confirms within a moment
                    ShowPhoneToolPage();
                }
            };
            return b;
        }
        _phoneMusicRow.Children.Add(MusicButton(IconPath(PrevGeo, 14, 10), "previous", "Previous"));
        _phoneMusicRow.Children.Add(MusicButton(_phonePlayIcon, "toggle", "Play or pause"));
        _phoneMusicRow.Children.Add(MusicButton(IconPath(NextGeo, 14, 10), "next", "Next"));
        _phoneLinked.Children.Add(_phoneMusicRow);
        _phoneLinked.Children.Add(_phoneConnectionsText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        var files = NotchButton.Pill(ShareBlue);
        files.Label = "Send files…";
        files.Click += SendFilesToPhone;
        var text = NotchButton.Pill();
        text.Label = "Send copied text";
        text.Click += SendClipboardToPhone;
        buttons.Children.Add(files);
        buttons.Children.Add(text);
        _phoneLinked.Children.Add(buttons);

        _phoneNewsTitle.Margin = new Thickness(2, 14, 0, 6);
        _phoneLinked.Children.Add(_phoneNewsTitle);
        _phoneLinked.Children.Add(_phoneNewsRows);

        _phoneUnlinked.Children.Add(_phoneUnlinkedText);
        _phoneUnlinkedButton = NotchButton.Pill(ShareBlue);
        _phoneUnlinkedButton.Margin = new Thickness(0, 10, 0, 0);
        _phoneUnlinkedButton.HorizontalAlignment = HorizontalAlignment.Left;
        _phoneUnlinkedButton.Click += () => _phoneUnlinkedAction();
        _phoneUnlinked.Children.Add(_phoneUnlinkedButton);

        _phoneToolPage.Children.Add(_phoneLinked);
        _phoneToolPage.Children.Add(_phoneUnlinked);
        return _phoneToolPage;
    }

    /// <summary>Fills the page with what the phone said last.</summary>
    void ShowPhoneToolPage()
    {
        var phone = _myPhone;
        _phoneLinked.IsVisible = phone != null;
        _phoneUnlinked.IsVisible = phone == null;
        if (phone == null)
        {
            ExplainPhoneLink();
            return;
        }

        bool there = _friends.OwnDevicesNow.Any(d => d.Id == phone.Id);
        var parts = new List<string> { there ? "Connected" : "Last seen " + Ago(DateTime.UtcNow - _myPhoneSeen) };
        if (_myPhoneBattery != null) parts.Add(_myPhoneCharging ? $"{_myPhoneBattery} · charging" : _myPhoneBattery);
        _phoneName.Text = phone.Name;
        _phoneLine.Text = string.Join(" · ", parts);
        _phoneSongText.Text = _myPhoneSong == null ? "" : "♪  " + _myPhoneSong + (_myPhonePlaying ? "" : "  (paused)");
        _phoneSongText.IsVisible = _myPhoneSong != null;
        _phoneMusicRow.IsVisible = _myPhoneSong != null;
        _phonePlayIcon.Data = _myPhonePlaying ? PauseGeo : PlayGeo;
        _phoneConnectionsText.Text = string.Join(" · ", _myPhoneConnections);
        _phoneConnectionsText.IsVisible = _myPhoneConnections.Count > 0;

        _phoneNewsRows.Children.Clear();
        foreach (var news in _phoneNews.Take(PhoneNewsShown).ToList())
        {
            var (title, detail, when, key) = news;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(2, 0) };
            row.Children.Add(new TextBlock
            {
                Text = detail is { Length: > 0 } ? $"{title} – {detail}" : title,
                FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var age = new TextBlock { Text = Ago(DateTime.UtcNow - when), FontSize = 11, Foreground = Dim, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(age, 1);
            row.Children.Add(age);
            // Closed here, gone on the phone too (if it can be cleared there).
            var close = new NotchButton(new TextBlock { Text = "\uE711", FontFamily = IconFont, FontSize = 10, Foreground = Dim })
            {
                Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(5), VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(close, "Close, here and on the phone");
            close.Click += () =>
            {
                // The page keeps its height while open: shrinking would leave the pointer outside, and the notch would close.
                _phoneLinked.MinHeight = Math.Max(_phoneLinked.MinHeight, _phoneLinked.Bounds.Height);
                _phoneNews.Remove(news);
                if (key != null && _myPhone is { } phone) _friends.DismissOnPhone(phone, key);
                ShowPhoneToolPage();
            };
            Grid.SetColumn(close, 2);
            row.Children.Add(close);
            _phoneNewsRows.Children.Add(row);
        }
        _phoneNewsTitle.IsVisible = _phoneNews.Count > 0;
    }

    /// <summary>No phone yet: says what is missing for the two to link, with the one thing to do about it.</summary>
    void ExplainPhoneLink()
    {
        if (_account is not { SignedIn: true } account)
        {
            _phoneUnlinkedText.Text = "Your phone links by itself, no code needed: sign in to the same Holm account here and in the Holm app on the phone.";
            _phoneUnlinkedButton.Label = "Sign in…";
            _phoneUnlinkedAction = () => ShowHub("profile");
        }
        else if (account.IdentityPending)
        {
            // This PC kept its own friend code beside the account's: the phone, which takes the account's, can't see it.
            _phoneUnlinkedText.Text = "This PC still has a friend code of its own next to your account's, so your phone can't find it. Say which one to use, and it links.";
            _phoneUnlinkedButton.Label = "Choose…";
            _phoneUnlinkedAction = AskAccountIdentity;
        }
        else
        {
            _phoneUnlinkedText.Text = $"Signed in as {account.Email}. Open Holm on your phone, signed in to the same account: it shows up here within a minute.";
            _phoneUnlinkedButton.Label = "Look again";
            _phoneUnlinkedAction = () => { _friends.AskForDevices(); ShowPeek(null, "Asked your devices", "", null); };
            _friends.AskForDevices(); // the phone answers if it runs
        }
    }

    static string Ago(TimeSpan span) => span.TotalMinutes < 1 ? "now"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
        : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
        : $"{(int)span.TotalDays} d ago";

    /// <summary>Keeps what the phone said for its page (called for every status, before any pop-out).</summary>
    void NotePhone(OwnDevice phone, PhoneStatus status)
    {
        _myPhone = phone;
        _myPhoneSeen = DateTime.UtcNow;
        switch (status.Kind)
        {
            case PhoneStatus.Status:
                _myPhoneBattery = status.Title;
                _myPhoneCharging = status.On;
                // The status names the song only while it plays: paused, the last one stays (to play it on).
                if (status.Detail != null) _myPhoneSong = status.Detail;
                _myPhonePlaying = status.Detail != null;
                _myPhoneConnections = status.Connections();
                break;
            case PhoneStatus.Music:
                if (status.On) _myPhoneSong = status.Detail is { } artist ? $"{status.Title} · {artist}" : status.Title;
                _myPhonePlaying = status.On;
                break;
            case PhoneStatus.Battery:
                _myPhoneBattery = status.Detail ?? "100%";
                _myPhoneCharging = true;
                break;
            case PhoneStatus.Notification or PhoneStatus.Call when status.On:
                _phoneNews.Insert(0, (status.Kind == PhoneStatus.Call ? $"Call from {status.Title}" : status.Title, status.Detail, DateTime.UtcNow, status.Key));
                if (_phoneNews.Count > 20) _phoneNews.RemoveAt(_phoneNews.Count - 1);
                break;
        }
        if (_tool == Tool.Phone) ShowPhoneToolPage();
    }

    ShareTarget? PhoneTarget => _myPhone is { } phone ? new ShareTarget(null, Device: phone) : null;

    async void SendFilesToPhone()
    {
        if (PhoneTarget is not { } target || _busy) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose files for your phone", AllowMultiple = true });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count > 0) await SendFilesAsync(target, paths);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("Phone files: " + ex.GetType().Name); }
    }

    async void SendClipboardToPhone()
    {
        if (PhoneTarget is not { } target || _busy || Clipboard is not { } clipboard) return;
        try
        {
            if (await PrivacyOfClipboardAsync(clipboard) != ClipPrivacy.Keep)
            {
                ShowPeek(null, "That's private (a password?) – not sent", "", null);
                return;
            }
            string? text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) { ShowPeek(null, "Copy some text first", "", null); return; }
            if (Encoding.UTF8.GetByteCount(text) > FileTransfer.MaxTextBytes) { ShowPeek(null, "That text is too long", "Send it as a file", Dim); return; }
            Directory.CreateDirectory(FolderPack.TempFolder);
            string path = Path.Combine(FolderPack.TempFolder, "Beamed text.txt");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(FolderPack.TempFolder, $"Beamed text ({n}).txt");
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
            await BeamTextFileAsync(target, path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("Phone text: " + ex.GetType().Name); } // never the text
    }
}
