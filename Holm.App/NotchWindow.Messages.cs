using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;
using WinNotch.Notify;

namespace Holm.App;

/// <summary>
/// Messages from chat and mail apps (WhatsApp, Discord, Outlook, …): they pop out of the notch, count on a badge in
/// the closed notch (and beside the music), and wait in the open notch until you look at the app or click them
/// (the WPF app's MessagesService and its parts of MainWindow.xaml.cs). Quiet hours keep them from popping out.
/// </summary>
public sealed partial class NotchWindow
{
    // App icons in the closed notch while notifications wait: as big as the album art (20) in the 30-high badge, a
    // little smaller next to the art in the 32-high media notch, so they read at a glance.
    const double BadgeLogoSize = 20, MediaBadgeLogoSize = 18;
    const string ChatGlyph = "";

    INotificationsPlatform _notifications = null!;
    Inbox _chat = null!;
    IReadOnlyList<ChatMessage> _messages = Array.Empty<ChatMessage>();
    bool _peekMessage, _notificationsRunning, _readingNotifications, _readNotificationsAgain;
    readonly Dictionary<string, Bitmap?> _logoCache = new();

    // Badge: nothing playing, unread messages
    readonly Grid _badgeView = new() { Margin = new Thickness(10, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
    readonly StackPanel _badgeLogoList = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
    readonly Border _badgePrivacyHost = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsVisible = false };
    readonly TextBlock _badgeText = CountText();
    Border _badgePill = null!;

    // Message: one just came in
    readonly Grid _messageView = new() { Margin = new Thickness(14, 0, 18, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
    readonly Border _messageLogoHost = new() { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _messageTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _messageText = new() { FontSize = 12, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };

    // Beside the music
    readonly StackPanel _mediaBadgeLogos = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Look.Pick(0, 6), 0) };
    readonly TextBlock _mediaBadgeText = CountText();
    Border _mediaBadge = null!;

    // In the open notch
    readonly TextBlock _quietNote = new() { FontSize = 11, Foreground = Dim, Margin = new Thickness(2, 12, 0, -6), IsVisible = false };
    NotchButton _clearNotificationsBtn = null!;
    readonly StackPanel _messageRows = new() { Margin = new Thickness(0, 6, 0, 0) };

    static TextBlock CountText() => new()
    {
        FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    static Border CountPill(TextBlock text) => new()
    {
        Background = Brush(Look.Pick("#FF3A3B3E", "#FF5B5FC7")), CornerRadius = new CornerRadius(Look.Pick(3, 8)), Height = 16, MinWidth = 16, Padding = new Thickness(5, 0),
        VerticalAlignment = VerticalAlignment.Center, Child = text,
    };

    void InitMessages(IPlatformServices platform)
    {
        _notifications = platform.Notifications;
        _chat = new Inbox(app => !_settings.MutedApps.Contains(app));
        _chat.Changed += OnMessagesChanged;
        _chat.OtherArrived += OnOtherNotification;
        _notifications.Changed += () => Dispatcher.UIThread.Post(() => _ = ReadNotificationsAsync());

        // The closed notch's badge: the count always at the left, then the apps' logos (the instrument look shows
        // only the count), and the dots at the right.
        _badgePill = CountPill(_badgeText);
        _badgePill.Margin = new Thickness(0, 0, 7, 0);
        _badgeView.Children.Add(_badgePill);
        Grid.SetColumn(_badgeLogoList, 1);
        _badgeView.Children.Add(_badgeLogoList);
        Grid.SetColumn(_badgePrivacyHost, 2);
        _badgeView.Children.Add(_badgePrivacyHost);

        _messageView.Children.Add(_messageLogoHost);
        var texts = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_messageTitle);
        texts.Children.Add(_messageText);
        Grid.SetColumn(texts, 1);
        _messageView.Children.Add(texts);
        foreach (var view in new Control[] { _badgeView, _messageView })
        {
            view.Opacity = 0;
            view.IsHitTestVisible = false;
            _islandContent.Children.Add(view);
        }

        // Beside the music: the count, left of the logos, before the timer and the equalizer.
        _mediaBadge = CountPill(_mediaBadgeText);
        _mediaBadge.Margin = new Thickness(0, 0, Look.Pick(9, 7), 0);
        _mediaBadge.IsVisible = false;
        _compactView.Children.Remove(_eqPanel);
        _compactView.Children.Remove(_compactTimer);
        var compactRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _eqPanel.HorizontalAlignment = HorizontalAlignment.Left;
        _compactTimer.HorizontalAlignment = HorizontalAlignment.Left;
        compactRight.Children.Add(_mediaBadge);
        compactRight.Children.Add(_mediaBadgeLogos);
        compactRight.Children.Add(_compactTimer);
        compactRight.Children.Add(_eqPanel);
        _compactView.Children.Add(compactRight);

        var clearLabel = new StackPanel { Orientation = Orientation.Horizontal };
        clearLabel.Children.Add(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
        clearLabel.Children.Add(new TextBlock { Text = "Clear all", FontSize = 11, Foreground = Dim, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _clearNotificationsBtn = new NotchButton(clearLabel)
        {
            Rest = Look.InkA(0x14), CornerRadius = new CornerRadius(Look.Pick(3, 5)), Height = 24, Padding = new Thickness(6, 0),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), IsVisible = false,
        };
        ToolTip.SetTip(_clearNotificationsBtn, "Clear message notifications");
        _clearNotificationsBtn.Click += () =>
        {
            _chat.MarkRead();
            _messages = _chat.Messages;
            _peekActive = false;
            _peekTimer.Stop();
            RefreshMessages();
            ApplyState(animate: true);
        };
    }

    void StartMessages()
    {
        _chat.EveryApp = _settings.AllNotifications;
        if (_settings.Messages || _settings.AllNotifications) _ = StartNotificationsAsync(ask: true);
    }

    async System.Threading.Tasks.Task StartNotificationsAsync(bool ask)
    {
        _notificationsRunning = true;
        await _notifications.StartAsync(ask);
        await ReadNotificationsAsync();
    }

    void StopNotifications()
    {
        _notificationsRunning = false;
        _notifications.Stop();
        _chat.Reset();
    }

    async System.Threading.Tasks.Task ReadNotificationsAsync()
    {
        if (!_notificationsRunning) return;
        if (_readingNotifications) { _readNotificationsAgain = true; return; }
        _readingNotifications = true;
        try
        {
            do
            {
                _readNotificationsAgain = false;
                var snapshot = await _notifications.ReadAsync();
                if (_notificationsRunning) _chat.Apply(snapshot);
            } while (_readNotificationsAgain && _notificationsRunning);
        }
        catch (Exception ex) { Diag.Log("Reading notifications failed: " + ex.Message); }
        finally { _readingNotifications = false; }
    }

    /// <summary>Another app's notification, shown at the top in the notch like a message (Settings › Pop-outs).</summary>
    void OnOtherNotification(ChatMessage notification)
    {
        if (!_settings.AllNotifications) return;
        if (Silenced(notification)) { Diag.Log("Quiet hours: no pop-out"); return; }
        ShowMessage(notification);
        ApplyState(animate: true);
    }

    void OnMessagesChanged(ChatMessage? arrived)
    {
        if (!_settings.Messages) return; // running only for every app's pop-outs
        // Already looking at that app: its messages aren't unread. (MarkRead calls back here with them cleared.)
        if (AppInFront() is { } front && _chat.Messages.Any(m => m.App == front.Key)) { _chat.MarkRead(front.Key); return; }

        _messages = _chat.Messages;
        Diag.Log($"Messages: {_chat.UnreadCount} unread" + (arrived != null ? $", new one from {arrived.App}" : ""));
        RefreshMessages();
        if (arrived != null && Silenced(arrived)) Diag.Log("Quiet hours: no pop-out");
        else if (arrived != null) ShowMessage(arrived);
        ApplyState(animate: true);
    }

    /// <summary>The messenger you're looking at, if any: its messages aren't unread.</summary>
    Messenger? AppInFront()
    {
        if (ChosenScreen() is not { } screen) return null;
        string name = _foreground.Read(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height).ProcessName;
        return Messengers.ForProcess(name) ?? Messengers.ForApp(name);
    }

    /// <summary>Called with the fullscreen check: a messenger in front reads its messages.</summary>
    void MarkFrontRead()
    {
        if (_messages.Count > 0 && AppInFront() is { } front && _messages.Any(m => m.App == front.Key)) _chat.MarkRead(front.Key);
    }

    /// <summary>Pops out a message that just came in. The caller applies the state.</summary>
    void ShowMessage(ChatMessage message)
    {
        if (_hiddenForFullscreen || _state == NotchState.Expanded) return;
        _updateNotification = false;
        _messageLogoHost.Child = AppLogo(message.App, 28);
        _messageTitle.Text = message.Title;
        _messageText.Text = message.Text;
        _messageText.IsVisible = message.Text.Length > 0;
        _peekActive = true;
        _peekMessage = true;
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(5); // long enough to read a line
        _peekTimer.Start();
    }

    /// <summary>The count on the badges and the messages in the open notch.</summary>
    void RefreshMessages()
    {
        _clearNotificationsBtn.IsVisible = _messages.Count > 0;
        int count = _messages.Sum(m => m.Count) + (HasUpdateNotification ? 1 : 0);
        string totalText = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        _badgeText.Text = _mediaBadgeText.Text = totalText;
        _mediaBadge.IsVisible = count > 0;
        _badgeLogoList.Children.Clear();
        _mediaBadgeLogos.Children.Clear();
        _messageRows.Children.Clear();
        if (count == 0) return;

        var groups = _messages
            .GroupBy(m => m.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                App = g.Key,
                Messages = g.ToList(),
                Latest = g.OrderByDescending(m => m.Time).First(),
                Display = g.Where(m => !m.CountOnly).OrderByDescending(m => m.Time).FirstOrDefault() ?? g.OrderByDescending(m => m.Time).First(),
            })
            .OrderByDescending(g => g.Latest.Time)
            .ToList();

        if (!Look.Instrument) // the instrument look shows only the count; the open notch says which apps wrote
        {
            PopulateAppLogos(_badgeLogoList, groups.Select(g => g.App), BadgeLogoSize);
            PopulateAppLogos(_mediaBadgeLogos, groups.Select(g => g.App), MediaBadgeLogoSize);
        }
        AddUpdateNotification();
        _mediaBadge.Background = _badgePill.Background = RedBrush;

        foreach (var group in groups)
        {
            var app = Messengers.Get(group.App);
            var message = group.Display;
            int appCount = group.Messages.Sum(m => m.Count);
            string title = !string.IsNullOrWhiteSpace(message.Title) ? message.Title : app?.Name ?? group.App;
            string text = message.CountOnly && string.IsNullOrWhiteSpace(message.Text) ? $"{appCount} unread" : message.Text;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Children.Add(AppLogo(group.App, 28));
            var textPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            textPanel.Children.Add(new TextBlock
            {
                Text = text, FontSize = 12, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
                IsVisible = !string.IsNullOrWhiteSpace(text),
            });
            Grid.SetColumn(textPanel, 1);
            row.Children.Add(textPanel);
            var pill = CountPill(CountText());
            ((TextBlock)pill.Child!).Text = appCount > 99 ? "99+" : appCount.ToString(CultureInfo.InvariantCulture);
            pill.Background = BrandBrush(group.App);
            Grid.SetColumn(pill, 2);
            row.Children.Add(pill);

            var button = NotchButton.Row(row);
            row.HorizontalAlignment = HorizontalAlignment.Stretch; // the count at the right edge
            button.Margin = new Thickness(0, 0, 0, 8);
            ToolTip.SetTip(button, "Open " + (app?.Name ?? group.App));
            var latest = message;
            button.Click += () => OpenMessage(latest);
            _messageRows.Children.Add(DismissibleNotification(button, () => _chat.MarkRead(group.App), pill));
        }
    }

    void OpenMessage(ChatMessage latest)
    {
        if (Messengers.Get(latest.App) is not { } app) return;
        _chat.MarkRead(app.Key);
        SetExpanded(false);
        // From a browser: back to the site. Otherwise the app, by the id its notifications came with.
        bool opened = false;
        try
        {
            if (latest.Site != null) { _shell.Open("https://" + latest.Site); opened = true; }
            else if (_chat.AppIdOf(app.Key) is { } id && _shell.OpenApp(id)) opened = true;
            else if (app.OpenUri != null) { _shell.Open(app.OpenUri); opened = true; }
        }
        catch (Exception ex) { Diag.Log("Open messenger: " + ex.GetType().Name); }
        if (!opened) ShowPeek("", "Can't open " + app.Name, "", null);
    }

    /// <summary>The row, with a close button that takes the count's place while the pointer is over it.</summary>
    Control DismissibleNotification(NotchButton notification, Action dismiss, Control? badge = null)
    {
        var host = new Grid();
        host.Children.Add(notification);
        var close = new NotchButton(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 10,10 M 10,0 L 0,10"), Stroke = Dim, StrokeThickness = 1.1,
            StrokeLineCap = PenLineCap.Round, Width = 10, Height = 10,
        })
        {
            Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 8), Opacity = 0, IsHitTestVisible = false,
        };
        ToolTip.SetTip(close, "Dismiss notification");
        host.Children.Add(close);
        host.PointerEntered += (_, _) => { close.Opacity = 1; close.IsHitTestVisible = true; if (badge != null) badge.Opacity = 0; };
        host.PointerExited += (_, _) => { close.Opacity = 0; close.IsHitTestVisible = false; if (badge != null) badge.Opacity = 1; };
        close.Click += dismiss;
        return host;
    }

    void PopulateAppLogos(Panel host, IEnumerable<string> apps, double size)
    {
        foreach (string app in apps)
        {
            var logo = AppLogo(app, size);
            logo.Margin = new Thickness(0, 0, 3, 0);
            host.Children.Add(logo);
        }
    }

    static IBrush BrandBrush(string app)
    {
        uint argb = Messengers.Get(app)?.Color ?? 0xFF5B5FC7;
        return new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
    }

    /// <summary>The app's own icon once we have it, a chat bubble (or envelope) in its colour until then.</summary>
    Control AppLogo(string app, double size)
    {
        if (LogoBitmap(app) is { } logo)
            return new Image { Source = logo, Width = size, Height = size };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = BrandBrush(app),
            Child = new TextBlock
            {
                Text = Messengers.Get(app)?.Glyph ?? ChatGlyph, FontFamily = IconFont, FontSize = size * 0.5, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    Bitmap? LogoBitmap(string app)
    {
        string appId = _chat.AppIdOf(app) ?? app;
        if (_logoCache.TryGetValue(appId, out var cached) && cached != null) return cached;
        if (_notifications.LogoOf(appId) is not { Length: > 0 } bytes) return null;
        try { return _logoCache[appId] = Bitmap.DecodeToWidth(new MemoryStream(bytes), 64); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _logoCache[appId] = null; return null; }
    }

    // ───────────── quiet hours ─────────────

    static readonly string[] QuietStarts = { "20:00", "21:00", "22:00", "23:00", "00:00" };
    static readonly string[] QuietEnds = { "05:00", "06:00", "07:00", "08:00", "09:00" };

    bool QuietNow => _settings.QuietHours && QuietHours.Parse(_settings.QuietFrom) is { } from && QuietHours.Parse(_settings.QuietUntil) is { } until
                     && QuietHours.IsQuiet(from, until, DateTime.Now);

    /// <summary>Whether a message that just came in stays in the badge instead of popping out.</summary>
    bool Silenced(ChatMessage message)
        => QuietHours.Silences(_settings.QuietHours, _settings.QuietFrom, _settings.QuietUntil, DateTime.Now, message.App, message.Title, _settings.QuietVips);

    /// <summary>A line in the open notch while it is quiet, so a missing pop-out never looks like a fault.</summary>
    void ShowQuietNote()
    {
        bool quiet = QuietNow;
        _quietNote.IsVisible = quiet;
        if (quiet) _quietNote.Text = _settings.QuietVips.Count > 0 ? $"Quiet until {_settings.QuietUntil} · VIPs still pop out" : $"Quiet until {_settings.QuietUntil}";
    }

    void FillQuietMenu(MenuItem menu)
    {
        menu.Items.Clear();
        var on = new MenuItem { Header = "Quiet hours", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.QuietHours, StaysOpenOnClick = true };
        on.Click += (_, _) => { _settings.QuietHours = !_settings.QuietHours; on.IsChecked = _settings.QuietHours; SaveQuiet(); };
        menu.Items.Add(on);
        menu.Items.Add(TimeChoices("From", QuietStarts, () => _settings.QuietFrom, t => _settings.QuietFrom = t));
        menu.Items.Add(TimeChoices("Until", QuietEnds, () => _settings.QuietUntil, t => _settings.QuietUntil = t));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Still pop out:", IsEnabled = false });
        foreach (var app in Messengers.All) menu.Items.Add(VipChoice(app.Name, QuietHours.AppPrefix + app.Key));
        // Who wrote lately, and whoever was let through before, so they can be taken off again.
        var people = QuietHours.RecentPeople(_chat.Messages.Where(m => !m.CountOnly).Select(m => (m.Title, m.Time)));
        foreach (var vip in _settings.QuietVips.Where(v => v.StartsWith(QuietHours.PersonPrefix, StringComparison.Ordinal)))
        {
            string name = vip[QuietHours.PersonPrefix.Length..];
            if (!people.Contains(name, StringComparer.OrdinalIgnoreCase)) people.Add(name);
        }
        if (people.Count == 0) return;
        menu.Items.Add(new Separator());
        foreach (var name in people) menu.Items.Add(VipChoice(name, QuietHours.PersonPrefix + name));
    }

    MenuItem TimeChoices(string label, string[] times, Func<string?> get, Action<string> set)
    {
        var menu = new MenuItem { Header = $"{label} {get()}" };
        foreach (var time in times)
        {
            var item = new MenuItem { Header = time, ToggleType = MenuItemToggleType.Radio, IsChecked = get() == time };
            item.Click += (_, _) => { set(time); SaveQuiet(); };
            menu.Items.Add(item);
        }
        return menu;
    }

    MenuItem VipChoice(string header, string vip)
    {
        var item = new MenuItem
        {
            Header = header, ToggleType = MenuItemToggleType.CheckBox, StaysOpenOnClick = true,
            IsChecked = _settings.QuietVips.Contains(vip, StringComparer.OrdinalIgnoreCase),
        };
        item.Click += (_, _) =>
        {
            bool add = !_settings.QuietVips.Contains(vip, StringComparer.OrdinalIgnoreCase);
            _settings.QuietVips.RemoveAll(v => string.Equals(v, vip, StringComparison.OrdinalIgnoreCase));
            if (add) _settings.QuietVips.Add(vip);
            item.IsChecked = add;
            SaveQuiet();
        };
        return item;
    }

    void SaveQuiet()
    {
        _settings.Save();
        ShowQuietNote();
        if (_hovered && _state == NotchState.Expanded) ApplyState(animate: true);
    }

    // ───────────── menus and settings ─────────────

    /// <summary>Messages › Show messages, one switch per app, and what's missing for full messages.</summary>
    void FillMessagesMenu(MenuItem menu)
    {
        menu.Items.Clear();
        var all = new MenuItem { Header = "Show messages", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.Messages };
        all.Click += async (_, _) =>
        {
            _settings.Messages = !_settings.Messages;
            _settings.Save();
            if (!_settings.Messages && !_settings.AllNotifications) { StopNotifications(); return; }
            await StartNotificationsAsync(ask: true);
            // Once refused, Windows doesn't ask again; the switch is in Settings.
            if (_notifications.Access == NotificationAccess.Denied) _notifications.OpenAccessSettings();
        };
        menu.Items.Add(all);
        menu.Items.Add(new Separator());
        foreach (var app in Messengers.All)
        {
            var item = new MenuItem
            {
                Header = app.Name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = !_settings.MutedApps.Contains(app.Key), IsEnabled = _settings.Messages,
            };
            item.Click += (_, _) =>
            {
                bool show = _settings.MutedApps.Contains(app.Key);
                _settings.MutedApps.Remove(app.Key);
                if (!show) _settings.MutedApps.Add(app.Key);
                _settings.Save();
                _chat.Refilter();
                _ = ReadNotificationsAsync();
            };
            menu.Items.Add(item);
        }
        if (!_settings.Messages || _notifications.AccessHint is not { } hint) return;
        menu.Items.Add(new Separator());
        var info = new MenuItem { Header = hint, IsEnabled = _notifications.Access == NotificationAccess.Denied };
        info.Click += (_, _) => _notifications.OpenAccessSettings();
        menu.Items.Add(info);
    }

    async void SetAllNotifications(bool on)
    {
        _settings.AllNotifications = on;
        _settings.Save();
        _chat.EveryApp = on;
        if (!on)
        {
            if (!_settings.Messages) StopNotifications();
            return;
        }
        if (!_notificationsRunning) await StartNotificationsAsync(ask: true);
        if (_notifications.Access == NotificationAccess.Denied) _notifications.OpenAccessSettings();
    }

    /// <summary>The dots sit in the badge while it shows, beside the content otherwise.</summary>
    void PlacePrivacyDots(bool badge)
    {
        var host = badge ? _badgePrivacyHost : _privacyHost;
        if (_privacyDots.Parent is Border previous && previous != host) previous.Child = null;
        if (host.Child != _privacyDots) host.Child = _privacyDots;
        bool visible = SensorsInUse.Count > 0 && !_hovered;
        _privacyHost.IsVisible = visible && !badge;
        _badgePrivacyHost.IsVisible = visible && badge;
    }
}
