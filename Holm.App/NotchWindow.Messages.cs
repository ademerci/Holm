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
    const double BadgeLogoSize = 20, MediaBadgeLogoSize = 18, BadgeLogoSpacing = 3;
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
    readonly StackPanel _mediaBadgeLogos = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
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

        // With no media, the standalone badge leads with the count and ends with the privacy dots.
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

        // Beside the music: the logos and count, before the timer and the equalizer.
        _mediaBadge = CountPill(_mediaBadgeText);
        _mediaBadge.Margin = new Thickness(0, 0, Look.Pick(9, 7), 0);
        _mediaBadge.IsVisible = false;
        _compactView.Children.Remove(_eqPanel);
        _compactView.Children.Remove(_compactTimer);
        var compactRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _eqPanel.HorizontalAlignment = HorizontalAlignment.Left;
        _compactTimer.HorizontalAlignment = HorizontalAlignment.Left;
        compactRight.Children.Add(_verityCompact);
        compactRight.Children.Add(_mediaBadgeLogos);
        compactRight.Children.Add(_mediaBadge);
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
        _closedUpdateBadge = _mediaUpdateBadge = null;
        _updateRowStatus = null;
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
            logo.Margin = new Thickness(0, 0, BadgeLogoSpacing, 0);
            host.Children.Add(logo);
        }
    }

    static IBrush BrandBrush(string app)
    {
        uint argb = Messengers.Get(app)?.Color ?? 0xFF5B5FC7;
        return new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
    }

    // Messenger marks from Simple Icons (CC0), https://github.com/simple-icons/simple-icons/tree/11.15.0/icons.
    static readonly Dictionary<string, Geometry> MessengerMarks = new()
    {
        ["discord"] = Geometry.Parse("M20.317 4.3698a19.7913 19.7913 0 0 0 -4.8851-1.5152.0741.0741 0 0 0 -.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 0 0 -.0785-.037 19.7363 19.7363 0 0 0 -4.8852 1.515.0699.0699 0 0 0 -.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 0 0 .0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 0 0 .0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 0 0 -.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 0 1 -.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 0 1 .0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 0 1 .0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 0 1 -.0066.1276 12.2986 12.2986 0 0 1 -1.873.8914.0766.0766 0 0 0 -.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 0 0 .0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 0 0 .0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 0 0 -.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z"),
        ["whatsapp"] = Geometry.Parse("M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413Z"),
        ["outlook"] = Geometry.Parse("M7.88 12.04q0 .45-.11.87-.1.41-.33.74-.22.33-.58.52-.37.2-.87.2t-.85-.2q-.35-.21-.57-.55-.22-.33-.33-.75-.1-.42-.1-.86t.1-.87q.1-.43.34-.76.22-.34.59-.54.36-.2.87-.2t.86.2q.35.21.57.55.22.34.31.77.1.43.1.88zM24 12v9.38q0 .46-.33.8-.33.32-.8.32H7.13q-.46 0-.8-.33-.32-.33-.32-.8V18H1q-.41 0-.7-.3-.3-.29-.3-.7V7q0-.41.3-.7Q.58 6 1 6h6.5V2.55q0-.44.3-.75.3-.3.75-.3h12.9q.44 0 .75.3.3.3.3.75V10.85l1.24.72h.01q.1.07.18.18.07.12.07.25zm-6-8.25v3h3v-3zm0 4.5v3h3v-3zm0 4.5v1.83l3.05-1.83zm-5.25-9v3h3.75v-3zm0 4.5v3h3.75v-3zm0 4.5v2.03l2.41 1.5 1.34-.8v-2.73zM9 3.75V6h2l.13.01.12.04v-2.3zM5.98 15.98q.9 0 1.6-.3.7-.32 1.19-.86.48-.55.73-1.28.25-.74.25-1.61 0-.83-.25-1.55-.24-.71-.71-1.24t-1.15-.83q-.68-.3-1.55-.3-.92 0-1.64.3-.71.3-1.2.85-.5.54-.75 1.3-.25.74-.25 1.63 0 .85.26 1.56.26.72.74 1.23.48.52 1.17.81.69.3 1.56.3zM7.5 21h12.39L12 16.08V17q0 .41-.3.7-.29.3-.7.3H7.5zm15-.13v-7.24l-5.9 3.54Z"),
        ["telegram"] = Geometry.Parse("M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z"),
        ["signal"] = Geometry.Parse("m9.12.35.27 1.09a10.845 10.845 0 0 0-3.015 1.248l-.578-.964A11.955 11.955 0 0 1 9.12.35zm5.76 0-.27 1.09a10.845 10.845 0 0 1 3.015 1.248l.581-.964A11.955 11.955 0 0 0 14.88.35zM1.725 5.797A11.955 11.955 0 0 0 .351 9.119l1.09.27A10.845 10.845 0 0 1 2.69 6.374zm-.6 6.202a10.856 10.856 0 0 1 .122-1.63l-1.112-.168a12.043 12.043 0 0 0 0 3.596l1.112-.169A10.856 10.856 0 0 1 1.125 12zm17.078 10.275-.578-.964a10.845 10.845 0 0 1-3.011 1.247l.27 1.091a11.955 11.955 0 0 0 3.319-1.374zM22.875 12a10.856 10.856 0 0 1-.122 1.63l1.112.168a12.043 12.043 0 0 0 0-3.596l-1.112.169a10.856 10.856 0 0 1 .122 1.63zm.774 2.88-1.09-.27a10.845 10.845 0 0 1-1.248 3.015l.964.581a11.955 11.955 0 0 0 1.374-3.326zm-10.02 7.875a10.952 10.952 0 0 1-3.258 0l-.17 1.112a12.043 12.043 0 0 0 3.597 0zm7.125-4.303a10.914 10.914 0 0 1-2.304 2.302l.668.906a12.019 12.019 0 0 0 2.542-2.535zM18.45 3.245a10.914 10.914 0 0 1 2.304 2.304l.906-.675a12.019 12.019 0 0 0-2.535-2.535zM3.246 5.549A10.914 10.914 0 0 1 5.55 3.245l-.675-.906A12.019 12.019 0 0 0 2.34 4.874zm19.029.248-.964.577a10.845 10.845 0 0 1 1.247 3.011l1.091-.27a11.955 11.955 0 0 0-1.374-3.318zM10.371 1.246a10.952 10.952 0 0 1 3.258 0L13.8.134a12.043 12.043 0 0 0-3.597 0zM3.823 21.957 1.5 22.5l.542-2.323-1.095-.257-.542 2.323a1.125 1.125 0 0 0 1.352 1.352l2.321-.532zm-2.642-3.041 1.095.255.375-1.61a10.828 10.828 0 0 1-1.21-2.952l-1.09.27a11.91 11.91 0 0 0 1.106 2.852zm5.25 2.437-1.61.375.255 1.095 1.185-.275a11.91 11.91 0 0 0 2.851 1.106l.27-1.091a10.828 10.828 0 0 1-2.943-1.217zM12 2.25a9.75 9.75 0 0 0-8.25 14.938l-.938 4 4-.938A9.75 9.75 0 1 0 12 2.25z"),
        ["slack"] = Geometry.Parse("M5.042 15.165a2.528 2.528 0 0 1-2.52 2.523A2.528 2.528 0 0 1 0 15.165a2.527 2.527 0 0 1 2.522-2.52h2.52v2.52zM6.313 15.165a2.527 2.527 0 0 1 2.521-2.52 2.527 2.527 0 0 1 2.521 2.52v6.313A2.528 2.528 0 0 1 8.834 24a2.528 2.528 0 0 1-2.521-2.522v-6.313zM8.834 5.042a2.528 2.528 0 0 1-2.521-2.52A2.528 2.528 0 0 1 8.834 0a2.528 2.528 0 0 1 2.521 2.522v2.52H8.834zM8.834 6.313a2.528 2.528 0 0 1 2.521 2.521 2.528 2.528 0 0 1-2.521 2.521H2.522A2.528 2.528 0 0 1 0 8.834a2.528 2.528 0 0 1 2.522-2.521h6.312zM18.956 8.834a2.528 2.528 0 0 1 2.522-2.521A2.528 2.528 0 0 1 24 8.834a2.528 2.528 0 0 1-2.522 2.521h-2.522V8.834zM17.688 8.834a2.528 2.528 0 0 1-2.523 2.521 2.527 2.527 0 0 1-2.52-2.521V2.522A2.527 2.527 0 0 1 15.165 0a2.528 2.528 0 0 1 2.523 2.522v6.312zM15.165 18.956a2.528 2.528 0 0 1 2.523 2.522A2.528 2.528 0 0 1 15.165 24a2.527 2.527 0 0 1-2.52-2.522v-2.522h2.52zM15.165 17.688a2.527 2.527 0 0 1-2.52-2.523 2.526 2.526 0 0 1 2.52-2.52h6.313A2.527 2.527 0 0 1 24 15.165a2.528 2.528 0 0 1-2.522 2.523h-6.313z"),
    };

    /// <summary>The messenger's brand mark, its system icon, or a chat/mail glyph.</summary>
    Control AppLogo(string app, double size)
    {
        if (MessengerMarks.TryGetValue(app, out var mark))
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = BrandBrush(app),
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = mark, Fill = Brushes.White, Stretch = Stretch.Uniform,
                    Width = size * (app == "discord" ? 0.76 : 0.66), Height = size * (app == "discord" ? 0.58 : 0.66),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
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

    const string MutePopOutsLabel = "Mute pop-outs";
    static readonly string[] QuietStarts = { "20:00", "21:00", "22:00", "23:00", "00:00" };
    static readonly string[] QuietEnds = { "05:00", "06:00", "07:00", "08:00", "09:00" };

    bool QuietNow => _settings.QuietHours && QuietHours.Parse(_settings.QuietFrom) is { } from && QuietHours.Parse(_settings.QuietUntil) is { } until
                     && QuietHours.IsQuiet(from, until, DateTime.Now);

    /// <summary>Whether a message that just came in stays in the badge instead of popping out.</summary>
    bool Silenced(ChatMessage message)
        => _settings.MutePopOuts || QuietHours.Silences(_settings.QuietHours, _settings.QuietFrom, _settings.QuietUntil, DateTime.Now, message.App, message.Title, _settings.QuietVips);

    /// <summary>A line in the open notch while it is quiet, so a missing pop-out never looks like a fault.</summary>
    void ShowQuietNote()
    {
        bool quiet = QuietNow;
        _quietNote.IsVisible = quiet || _settings.MutePopOuts;
        if (_settings.MutePopOuts) _quietNote.Text = "Pop-outs muted · right-click to switch back on";
        else if (quiet) _quietNote.Text = _settings.QuietVips.Count > 0 ? $"Quiet until {_settings.QuietUntil} · VIPs still pop out" : $"Quiet until {_settings.QuietUntil}";
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

    void SetMutePopOuts(bool muted)
    {
        _settings.MutePopOuts = muted;
        SaveQuiet();
        _settingsWindow?.RefreshSwitch(MutePopOutsLabel);
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
