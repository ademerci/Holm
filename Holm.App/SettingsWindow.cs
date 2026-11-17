using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Holm.App;

/// <summary>One switch in the settings: its words, an icon, and how to read and apply it.</summary>
sealed record SettingsSwitch(string Header, string Glyph, string? Help, Func<bool> Get, Action<bool> Set, bool Default, string? ActionLabel = null, Action? RunAction = null);

/// <summary>A page of switches.</summary>
sealed record SettingsGroup(string Key, string Title, string Glyph, Color Color, string Subtitle, IReadOnlyList<SettingsSwitch> Switches);

/// <summary>
/// The Holm window's settings, as in the WPF app (WinNotch/SettingsWindow.cs): a sidebar with one page per group,
/// each a card of switches. Flipping a switch stages it; Apply sets them, Reset to defaults stages the defaults, and
/// closing with changes asks first. Dark and rounded on every system; the window draws its own frame.
/// </summary>
/// <summary>A page of the Holm window other than the switches (friends, chat, achievements). Told when it goes away.</summary>
abstract class HubView : Border
{
    /// <summary>The page is no longer shown: stop listening for events.</summary>
    public virtual void Detach() { }
}

/// <summary>An entry in the Holm window's sidebar.</summary>
/// <param name="Build">Makes the page, each time it's opened.</param>
/// <param name="Badge">A count to show beside the entry (unread messages), or 0.</param>
sealed record HubPage(string Key, string Glyph, string Title, string Subtitle, Func<HubView> Build, Func<int>? Badge = null);

sealed class SettingsWindow : Window
{
    static IBrush B(uint argb) => new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
    // The Holm window's palette is HubTheme's (the instrument look), so the settings and the friends pages match.
    static readonly IBrush Panel = HubTheme.Panel, Sidebar = HubTheme.Sidebar, Edge = HubTheme.Edge, Card = HubTheme.Card,
        Hover = HubTheme.Hover, Selected = HubTheme.Selected, Line = HubTheme.Line, Dim = HubTheme.Dim, Accent = HubTheme.Accent,
        OnAccent = HubTheme.OnAccent;

    readonly List<(SettingsSwitch Setting, ToggleSwitch Toggle)> _rows = new();
    readonly Dictionary<string, (Border Item, TextBlock Glyph, Border Badge, TextBlock BadgeText)> _navItems = new();
    readonly IReadOnlyList<HubPage> _social, _extra, _tools;
    HubView? _shownView;
    Control _footer = null!;
    readonly Dictionary<string, Control> _pages = new();
    readonly Dictionary<string, (string Title, string Subtitle)> _titles = new();
    readonly TextBlock _pageTitle = new() { FontSize = 24, FontWeight = FontWeight.SemiBold };
    // The instrument look drops the subtitle under the page title: the rows say what the page holds.
    readonly TextBlock _pageSubtitle = new() { Foreground = Dim, FontSize = 13, Margin = new Thickness(0, 2, 0, 0), IsVisible = !Look.Instrument };
    readonly Border _pageHost = new();
    readonly Button _apply;
    readonly Border _surface, _dialog;
    readonly Func<bool> _animations;
    bool _allowClose;

    public string Page { get; private set; } = "";

    const string ProfileKey = "profile";
    readonly ProfileEditor? _profile;
    readonly Func<Control>? _me;
    Border? _meCard;

    bool HasChanges => _rows.Any(r => r.Setting.Get() != r.Toggle.IsOn) || _profile?.HasChanges == true;

    public SettingsWindow(IReadOnlyList<SettingsGroup> groups, string version, Func<bool> animations,
                          IReadOnlyList<HubPage>? social = null, string? start = null, ProfileEditor? profile = null, Func<Control>? me = null,
                          IReadOnlyList<HubPage>? extra = null, IReadOnlyList<HubPage>? tools = null)
    {
        _tools = tools ?? Array.Empty<HubPage>();
        _extra = extra ?? Array.Empty<HubPage>();
        _animations = animations;
        _profile = profile;
        _me = me;
        _social = social ?? Array.Empty<HubPage>();
        Title = "Holm";
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        Width = 980;
        Height = 740;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Fonts.Text;
        FontSize = 13;
        Foreground = Brushes.White;
        Fonts.ApplyRendering(this);

        // ── sidebar ──
        var nav = new StackPanel();
        var sidebar = new DockPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 0, 18), Background = Brushes.Transparent };
        brand.Children.Add(new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Brushes.Black,
            BorderBrush = Edge, BorderThickness = new Thickness(1),
            Child = new Border { Width = 16, Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) },
        });
        var brandText = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "Holm", FontSize = 16, FontWeight = FontWeight.SemiBold });
        brandText.Children.Add(new TextBlock { Text = "Version " + version, FontSize = 11, Foreground = Dim });
        brand.Children.Add(brandText);
        brand.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        DockPanel.SetDock(brand, Dock.Top);
        sidebar.Children.Add(brand);
        if (me != null)
        {
            var card = new Border
            {
                Background = Card, CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 14),
                Cursor = new Cursor(StandardCursorType.Hand), Child = me(),
            };
            _meCard = card;
            ToolTip.SetTip(card, "Edit your profile");
            card.PointerReleased += (_, _) => Navigate(ProfileKey);
            DockPanel.SetDock(card, Dock.Top);
            sidebar.Children.Add(card);
        }
        sidebar.Children.Add(Scroller(nav));

        if (_social.Count > 0)
        {
            nav.Children.Add(Caption("Friends"));
            foreach (var page in _social)
            {
                _titles[page.Key] = (page.Title, page.Subtitle);
                nav.Children.Add(NavItem(page.Key, page.Glyph, page.Title));
            }
            nav.Children.Add(new Border { Height = 14 });
        }
        if (_tools.Count > 0)
        {
            nav.Children.Add(Caption("Tools"));
            foreach (var page in _tools)
            {
                _titles[page.Key] = (page.Title, page.Subtitle);
                nav.Children.Add(NavItem(page.Key, page.Glyph, page.Title));
            }
            nav.Children.Add(new Border { Height = 14 });
        }
        nav.Children.Add(Caption("Settings"));
        if (profile != null)
        {
            _titles[ProfileKey] = ("Profile", "How your friends see you");
            nav.Children.Add(NavItem(ProfileKey, "\uE77B", "Profile"));
            profile.Changed += RefreshApplyState;
            profile.Submit += () => { if (HasChanges) ApplyChanges(); };
            _pages[ProfileKey] = profile;
        }
        foreach (var group in groups)
        {
            _titles[group.Key] = (group.Title, group.Subtitle);
            nav.Children.Add(NavItem(group.Key, group.Glyph, group.Title));
            _pages[group.Key] = GroupPage(group);
        }
        // Pages among the settings that aren't switches (Updates): built when opened, without Apply.
        foreach (var page in _extra)
        {
            _titles[page.Key] = (page.Title, page.Subtitle);
            nav.Children.Add(NavItem(page.Key, page.Glyph, page.Title));
        }
        var sideBorder = new Border
        {
            Background = Sidebar, CornerRadius = new CornerRadius(14, 0, 0, 14), Padding = new Thickness(14, 16, 10, 16), Child = sidebar,
        };

        // ── content ──
        var content = new DockPanel { Margin = new Thickness(28, 18, 22, 20) };
        var header = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 16), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var titles = new StackPanel();
        titles.Children.Add(_pageTitle);
        titles.Children.Add(_pageSubtitle);
        header.Children.Add(titles);
        var close = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Card, Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Top, Child = Glyph("", 11),
        };
        ToolTip.SetTip(close, "Close (Esc)");
        close.PointerEntered += (_, _) => close.Background = Edge;
        close.PointerExited += (_, _) => close.Background = Card;
        close.PointerPressed += (_, e) => e.Handled = true; // not the start of a drag
        close.PointerReleased += (_, _) => Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);
        content.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var reset = Action("Reset to defaults");
        reset.Margin = new Thickness(0, 0, 10, 0);
        _apply = Action("Apply", primary: true);
        _apply.Padding = new Thickness(20, 6, 20, 6);
        reset.Click += (_, _) => ShowDialog("Reset settings?", "Restore the switches to their defaults? Click Apply afterwards to save them.",
            ("Cancel", () => { }), ("Reset to defaults", ResetDefaults));
        _apply.Click += (_, _) => ApplyChanges();
        footer.Children.Add(reset);
        footer.Children.Add(_apply);
        _footer = footer;
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        content.Children.Add(_pageHost);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("232,*") };
        columns.Children.Add(sideBorder);
        Grid.SetColumn(content, 1);
        columns.Children.Add(content);

        _surface = new Border
        {
            Background = Panel, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Margin = new Thickness(16), Child = columns,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 28, Color = Color.FromArgb(0x80, 0, 0, 0) }),
        };
        _dialog = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), CornerRadius = new CornerRadius(14), Margin = new Thickness(16),
            IsVisible = false,
        };
        var layers = new Grid();
        layers.Children.Add(_surface);
        layers.Children.Add(_dialog);
        Content = layers;

        Navigate(start != null && _titles.ContainsKey(start) ? start : _social.Count > 0 ? _social[0].Key : groups[0].Key);
        RefreshApplyState();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_dialog.IsVisible) HideDialog(); else Close();
        };
        Activated += (_, _) =>
        {
            if (!HasChanges && !_dialog.IsVisible) Refresh();
            (_shownView as ChatPage)?.Seen();
        };
        Closing += (_, e) =>
        {
            if (_allowClose || !HasChanges && !_dialog.IsVisible) return;
            e.Cancel = true;
            if (_dialog.IsVisible) return;
            ShowDialog("Unapplied changes", "Your changes have not been applied. Apply them before closing, or discard them?",
                ("Keep editing", () => { }),
                ("Discard", () => { _allowClose = true; Close(); }),
                ("Apply and close", () => { ApplyChanges(); if (!HasChanges) Close(); }));
        };
    }

    /// <summary>Opens a page: a friends page by its key, or a group of switches by its key.</summary>
    public void Navigate(string key)
    {
        if (!_titles.ContainsKey(key)) return;
        _shownView?.Detach();
        _shownView = null;
        bool settings = _pages.TryGetValue(key, out var page);
        if (!settings && _social.Concat(_tools).Concat(_extra).FirstOrDefault(p => p.Key == key) is { } social) page = _shownView = social.Build();
        Page = key;
        _pageHost.Child = page;
        (_pageTitle.Text, _pageSubtitle.Text) = _titles[key];
        _footer.IsVisible = settings;
        foreach (var (k, item) in _navItems)
        {
            bool on = k == key;
            item.Item.Background = on ? Selected : Brushes.Transparent;
            item.Glyph.Foreground = on ? Accent : Dim;
        }
        RefreshBadges();
    }

    /// <summary>Updates the counts beside the friends pages (unread messages).</summary>
    public void RefreshBadges()
    {
        foreach (var page in _social)
        {
            if (!_navItems.TryGetValue(page.Key, out var nav)) continue;
            int count = page.Badge?.Invoke() ?? 0;
            nav.BadgeText.Text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            nav.Badge.IsVisible = count > 0;
        }
    }

    /// <summary>The page shown now, if it's one of the friends pages.</summary>
    public HubView? ShownView => _shownView;

    /// <summary>Shows each switch as the setting stands now.</summary>
    public void Refresh()
    {
        foreach (var (setting, toggle) in _rows) toggle.Set(setting.Get(), animate: false);
        _profile?.Reload();
        if (_meCard != null && _me != null) _meCard.Child = _me(); // the name or picture may have changed
        RefreshApplyState();
    }

    void RefreshApplyState()
    {
        _apply.IsEnabled = HasChanges;
        _apply.Background = HasChanges ? Accent : Card;
        if (!Look.Instrument) return;
        _apply.Foreground = HasChanges ? OnAccent : Dim;
        foreach (string state in new[] { "PointerOver", "Pressed", "Disabled" }) _apply.Resources["ButtonForeground" + state] = _apply.Foreground;
    }

    Border NavItem(string key, string glyph, string title)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto") };
        var icon = Glyph(glyph, 15, Dim);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5 };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var badgeText = new TextBlock { FontSize = 11, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border
        {
            Background = B(0xFFFF453A), CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0),
            Child = badgeText, IsVisible = false, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);
        var item = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = grid,
        };
        item.PointerEntered += (_, _) => { if (Page != key) item.Background = Hover; };
        item.PointerExited += (_, _) => { if (Page != key) item.Background = Brushes.Transparent; };
        item.PointerReleased += (_, _) => Navigate(key);
        _navItems[key] = (item, icon, badge, badgeText);
        return item;
    }

    Control GroupPage(SettingsGroup group)
    {
        var rows = new StackPanel();
        for (int i = 0; i < group.Switches.Count; i++)
        {
            if (Look.Instrument) rows.Children.Add(new Border { Height = 1, Background = Line });
            else if (i > 0) rows.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(58, 0, 0, 0) });
            rows.Children.Add(Row(group.Switches[i], group.Color, first: i == 0, last: i == group.Switches.Count - 1));
        }
        // The instrument look: a plain list between hairlines, no card behind it.
        var card = Look.Instrument
            ? new Border { Child = rows, VerticalAlignment = VerticalAlignment.Top }
            : new Border { Background = Card, CornerRadius = new CornerRadius(12), Child = rows, VerticalAlignment = VerticalAlignment.Top };
        return Scroller(card);
    }

    Control Row(SettingsSwitch setting, Color color, bool first, bool last)
    {
        var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        toggle.Set(setting.Get(), animate: false);
        _rows.Add((setting, toggle));

        var row = new Grid { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), MinHeight = 56 };
        var hover = Look.Instrument ? new Border() : new Border { CornerRadius = new CornerRadius(first ? 12 : 0, first ? 12 : 0, last ? 12 : 0, last ? 12 : 0) };
        row.Children.Add(hover);
        var content = new Grid { Margin = Look.Pick(new Thickness(2, 11, 4, 11), new Thickness(14, 10, 16, 10)), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (!Look.Instrument) // the instrument look has no icon in front of each row
        {
            var tile = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(30 * 0.27), Background = new SolidColorBrush(color).ToImmutable(),
                Child = Glyph(setting.Glyph, 30 * 0.47), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(tile);
        }
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = setting.Header, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (setting.Help != null)
            words.Children.Add(new TextBlock { Text = setting.Help, Foreground = Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(words, 1);
        content.Children.Add(words);
        if (setting.RunAction != null && setting.ActionLabel != null)
        {
            content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var reset = Action(setting.ActionLabel);
            reset.Padding = new Thickness(10, 3, 10, 3);
            reset.MinHeight = 28;
            reset.Height = 28;
            reset.Margin = new Thickness(8, 0, 12, 0);
            reset.VerticalAlignment = VerticalAlignment.Center;
            reset.Click += (_, _) => setting.RunAction();
            // Keep the action button separate from the row toggle.
            reset.AddHandler(PointerReleasedEvent, (_, e) => e.Handled = true, Avalonia.Interactivity.RoutingStrategies.Bubble);
            Grid.SetColumn(reset, 2);
            content.Children.Add(reset);
            Grid.SetColumn(toggle, 3);
        }
        else Grid.SetColumn(toggle, 2);
        content.Children.Add(toggle);
        row.Children.Add(content);

        row.PointerEntered += (_, _) => hover.Background = Hover;
        row.PointerExited += (_, _) => hover.Background = null;
        row.PointerReleased += (_, e) => { if (e.Handled) return; toggle.Set(!toggle.IsOn, _animations()); RefreshApplyState(); };
        return row;
    }

    void ApplyChanges()
    {
        foreach (var (setting, toggle) in _rows)
        {
            if (setting.Get() == toggle.IsOn) continue;
            try { setting.Set(toggle.IsOn); }
            catch (Exception ex) { WinNotch.Diag.Error(ex); }
        }
        _profile?.Apply();
        Refresh();
    }

    void ResetDefaults()
    {
        foreach (var (setting, toggle) in _rows) toggle.Set(setting.Default, animate: false);
        RefreshApplyState();
    }

    /// <summary>A question over the window; each choice closes it and runs.</summary>
    void ShowDialog(string title, string message, params (string Label, Action Run)[] choices)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Dim, Margin = new Thickness(0, 10, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, run) in choices)
        {
            var button = Action(label);
            button.Padding = new Thickness(12, 6, 12, 6);
            button.Margin = new Thickness(6, 0, 0, 0);
            button.Click += (_, _) => { HideDialog(); run(); };
            actions.Children.Add(button);
        }
        content.Children.Add(actions);
        _dialog.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 34)), BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Width = 460,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 24, Color = Color.FromArgb(0x73, 0, 0, 0) }), Child = content,
        };
        _surface.Effect = new BlurEffect { Radius = 5 };
        _surface.IsEnabled = false;
        _dialog.IsVisible = true;
    }

    void HideDialog()
    {
        _dialog.IsVisible = false;
        _dialog.Child = null;
        _surface.Effect = null;
        _surface.IsEnabled = true;
    }

    static TextBlock Glyph(string glyph, double size, IBrush? foreground = null) => new()
    {
        Text = glyph, FontFamily = Fonts.Icons, FontSize = size, Foreground = foreground ?? Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    static TextBlock Caption(string title) => new()
    {
        Text = title.ToUpperInvariant(), Foreground = Dim, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0, 0, 6),
    };

    static ScrollViewer Scroller(Control content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0, 0, 6, 0),
    };

    /// <summary>The WPF app's action button (HubTheme's).</summary>
    static Button Action(string label, bool primary = false) => HubTheme.Action(label, primary);
}

/// <summary>
/// An on/off switch (the WPF app's ToggleSwitch): a pill that turns green with the knob sliding to the right, or in the
/// instrument look a square outline with a square knob, both white when on.
/// </summary>
sealed class ToggleSwitch : Control
{
    public static readonly StyledProperty<double> PositionProperty = AvaloniaProperty.Register<ToggleSwitch, double>(nameof(Position));

    static readonly Color Off = Look.Pick(Color.FromRgb(0x5C, 0x5E, 0x63), Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
        On = Look.Pick(Color.FromRgb(0xF2, 0xF2, 0xF2), Color.FromRgb(0x30, 0xD1, 0x58));

    static ToggleSwitch() => AffectsRender<ToggleSwitch>(PositionProperty);

    double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public bool IsOn { get; private set; }

    public void Set(bool on, bool animate)
    {
        IsOn = on;
        Transitions = animate
            ? new Transitions { new DoubleTransition { Property = PositionProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new BackEaseOut() } }
            : null;
        Position = on ? 1 : 0;
    }

    protected override Size MeasureOverride(Size availableSize) => Look.Instrument ? new(32, 18) : new(40, 24);

    public override void Render(DrawingContext dc)
    {
        double t = Math.Clamp(Position, 0, 1);
        if (Look.Instrument)
        {
            var ink = new SolidColorBrush(Color.FromRgb(
                (byte)(Off.R + (On.R - Off.R) * t), (byte)(Off.G + (On.G - Off.G) * t), (byte)(Off.B + (On.B - Off.B) * t)));
            dc.DrawRectangle(null, new Pen(ink, 1.5), new RoundedRect(new Rect(0.75, 0.75, 30.5, 16.5), 2));
            dc.DrawRectangle(ink, null, new Rect(3.5 + 14 * Position, 3.5, 11, 11));
            return;
        }
        var track = new SolidColorBrush(Color.FromArgb(
            (byte)(Off.A + (On.A - Off.A) * t), (byte)(Off.R + (On.R - Off.R) * t),
            (byte)(Off.G + (On.G - Off.G) * t), (byte)(Off.B + (On.B - Off.B) * t)));
        dc.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, 40, 24), 12));
        dc.DrawEllipse(Brushes.White, null, new Point(12 + 16 * Position, 12), 10, 10);
    }
}
