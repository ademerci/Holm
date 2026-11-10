using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WinNotch;

/// <summary>One switch in the settings: the hidden menu entry that holds its handler, an icon and a line of help.</summary>
internal sealed record SettingsSwitch(MenuItem Item, string Glyph, string? Help = null);

/// <summary>A page of switches in the Holm window.</summary>
internal sealed record SettingsGroup(string Key, string Title, string Glyph, Color Color, string Subtitle, IReadOnlyList<SettingsSwitch> Switches);

/// <summary>
/// The Holm window: a sidebar with your friends, chat and achievements on top and the settings below, one page at a
/// time. Switches and the profile stage a draft; Apply raises the hidden menu entries' handlers to save the changes.
/// </summary>
internal sealed class SettingsWindow : Window
{
    const string ProfileKey = "profile";

    bool _animate, _allowClose;
    readonly Button _apply;
    readonly Border _dialog, _surface, _pageHost;
    readonly FrameworkElement _footer;
    readonly TextBlock _pageTitle, _pageSubtitle;
    readonly StackPanel _nav;
    readonly Dictionary<string, (Border Item, TextBlock Glyph, Border Badge, TextBlock BadgeText)> _navItems = new();
    readonly Dictionary<string, FrameworkElement> _settingsPages = new();
    readonly Dictionary<string, (string Title, string Subtitle)> _titles = new();
    readonly IReadOnlyList<HubPage> _social;
    IInputElement? _previousFocus;
    HubView? _shownView;
    bool HasChanges => _rows.Any(r => r.Item.IsChecked != r.Switch.IsOn) || _profile?.HasChanges == true;
    readonly Action _sync;
    readonly ProfileEditor? _profile;
    readonly List<(MenuItem Item, ToggleSwitch Switch)> _rows = new();

    /// <summary>The page shown now (a key of a group, "profile", or a social page).</summary>
    public string Page { get; private set; } = "";

    /// <param name="sync">Checks the menu entries as the settings stand, before the switches are shown.</param>
    /// <param name="profile">Your profile, the first settings page, saved with the switches.</param>
    /// <param name="social">Friends, chat and achievements: the pages above the settings.</param>
    /// <param name="me">Your face and name for the top of the sidebar; clicking it opens the profile.</param>
    public SettingsWindow(IReadOnlyList<SettingsGroup> groups, string version, bool animate, Action sync, ProfileEditor? profile = null,
                          IReadOnlyList<HubPage>? social = null, Func<FrameworkElement>? me = null)
    {
        _animate = animate;
        _sync = sync;
        _profile = profile;
        _social = social ?? Array.Empty<HubPage>();
        sync();
        Title = "Holm";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Width = 980;
        Height = Math.Min(740, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; // the notch is topmost too; this must not open behind it
        FontFamily = HubTheme.Text;
        FontSize = 13;
        Foreground = Brushes.White;

        // ── sidebar ──
        _nav = new StackPanel();
        var sidebar = new DockPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 0, 18), Background = Brushes.Transparent };
        // The logo: a notch, a black pill at the top of a blue screen.
        brand.Children.Add(new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0xA0, 0xFF), Color.FromRgb(0x0A, 0x60, 0xE0), 90),
            Child = new Border
            {
                Width = 16, Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0),
            },
        });
        var brandText = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "Holm", FontSize = 16, FontWeight = FontWeights.SemiBold });
        brandText.Children.Add(new TextBlock { Text = "Version " + version, FontSize = 11, Foreground = HubTheme.Dim });
        brand.Children.Add(brandText);
        brand.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        DockPanel.SetDock(brand, Dock.Top);
        sidebar.Children.Add(brand);
        if (me != null)
        {
            var card = new Border
            {
                Background = HubTheme.Card, CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 14),
                Cursor = Cursors.Hand, Child = me(), ToolTip = "Edit your profile",
            };
            card.MouseLeftButtonUp += (_, _) => Navigate(ProfileKey);
            DockPanel.SetDock(card, Dock.Top);
            sidebar.Children.Add(card);
        }
        sidebar.Children.Add(HubTheme.Scroller(_nav));

        if (_social.Count > 0)
        {
            _nav.Children.Add(HubTheme.Caption("Friends"));
            foreach (var page in _social)
            {
                _titles[page.Key] = (page.Title, page.Subtitle);
                _nav.Children.Add(NavItem(page.Key, page.Glyph, page.Title));
            }
            _nav.Children.Add(new Border { Height = 14 });
        }
        _nav.Children.Add(HubTheme.Caption("Settings"));
        if (profile != null)
        {
            _titles[ProfileKey] = ("Profile", "How your friends see you");
            _nav.Children.Add(NavItem(ProfileKey, "", "Profile"));
            profile.Changed += RefreshApplyState;
            _settingsPages[ProfileKey] = profile;
        }
        foreach (var group in groups)
        {
            _titles[group.Key] = (group.Title, group.Subtitle);
            _nav.Children.Add(NavItem(group.Key, group.Glyph, group.Title));
            _settingsPages[group.Key] = GroupPage(group);
        }

        var sideBorder = new Border
        {
            Background = HubTheme.Sidebar, CornerRadius = new CornerRadius(14, 0, 0, 14), Padding = new Thickness(14, 16, 10, 16),
            Child = sidebar,
        };

        // ── content ──
        var content = new DockPanel { Margin = new Thickness(28, 18, 22, 20) };
        var header = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var titles = new StackPanel();
        _pageTitle = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold };
        _pageSubtitle = new TextBlock { Foreground = HubTheme.Dim, FontSize = 13, Margin = new Thickness(0, 2, 0, 0) };
        titles.Children.Add(_pageTitle);
        titles.Children.Add(_pageSubtitle);
        header.Children.Add(titles);
        var close = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = HubTheme.Card, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top, ToolTip = "Close (Esc)", Child = HubTheme.Glyph("", 11),
        };
        close.MouseEnter += (_, _) => close.Background = HubTheme.Edge;
        close.MouseLeave += (_, _) => close.Background = HubTheme.Card;
        close.MouseLeftButtonDown += (_, e) => e.Handled = true; // not the start of a drag
        close.MouseLeftButtonUp += (_, _) => Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top);
        content.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var reset = HubTheme.Action("Reset to defaults");
        reset.Margin = new Thickness(0, 0, 10, 0);
        var apply = _apply = HubTheme.Action("Apply", primary: true);
        apply.Padding = new Thickness(20, 6, 20, 6);
        reset.Click += (_, _) => ShowDialog("Reset settings?", "Restore the switches to their defaults? Click Apply afterwards to save them.",
            ("Cancel", () => { }), ("Reset to defaults", ResetDefaults));
        apply.Click += (_, _) => ApplyChanges();
        footer.Children.Add(reset);
        footer.Children.Add(apply);
        _footer = footer;
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        _pageHost = new Border();
        content.Children.Add(_pageHost);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.Children.Add(sideBorder);
        Grid.SetColumn(content, 1);
        columns.Children.Add(content);

        var layers = new Grid();
        _surface = new Border
        {
            Background = HubTheme.Panel,
            BorderBrush = HubTheme.Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(16), // room for the shadow and modal blur
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Opacity = 0.5 },
            Child = columns,
        };
        layers.Children.Add(_surface);
        // Covers the whole window, so only the background blurs behind a question.
        _dialog = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
            CornerRadius = new CornerRadius(14), Margin = new Thickness(16),
            Visibility = Visibility.Collapsed
        };
        KeyboardNavigation.SetTabNavigation(_dialog, KeyboardNavigationMode.Cycle);
        layers.Children.Add(_dialog);
        Content = layers;

        Navigate(_social.Count > 0 ? _social[0].Key : profile != null ? ProfileKey : groups[0].Key);
        RefreshApplyState();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_dialog.Visibility == Visibility.Visible) HideDialog(); else Close();
        };
        Activated += (_, _) => { if (!HasChanges && _dialog.Visibility != Visibility.Visible) Refresh(); };
        Closing += (_, e) =>
        {
            if (_allowClose || !HasChanges && _dialog.Visibility != Visibility.Visible) return;
            e.Cancel = true;
            if (_dialog.Visibility == Visibility.Visible) return;
            ShowDialog("Unapplied changes", "Your changes have not been applied. Apply them before closing, or discard them?",
                ("Keep editing", () => { }),
                ("Discard", () => { _allowClose = true; Close(); }),
                ("Apply and close", () => { ApplyChanges(); if (!HasChanges) Close(); }));
        };
        Closed += (_, _) => _shownView?.Detach();
    }

    /// <summary>Opens a page: a social one by its key, "profile", or a group of switches by its key.</summary>
    public void Navigate(string key)
    {
        if (!_titles.ContainsKey(key)) return;
        _shownView?.Detach();
        _shownView = null;
        Page = key;
        bool settings = _settingsPages.TryGetValue(key, out var page);
        if (!settings && _social.FirstOrDefault(p => p.Key == key) is { } social)
        {
            _shownView = social.Build();
            page = _shownView;
        }
        _pageHost.Child = page;
        (_pageTitle.Text, _pageSubtitle.Text) = _titles[key];
        _footer.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (k, nav) in _navItems)
        {
            bool on = k == key;
            nav.Item.Background = on ? HubTheme.Selected : Brushes.Transparent;
            nav.Glyph.Foreground = on ? HubTheme.Accent : HubTheme.Dim;
        }
        RefreshBadges();
    }

    /// <summary>Updates the counts beside the social pages (unread messages).</summary>
    public void RefreshBadges()
    {
        foreach (var page in _social)
        {
            if (!_navItems.TryGetValue(page.Key, out var nav)) continue;
            int count = page.Badge?.Invoke() ?? 0;
            nav.BadgeText.Text = count > 99 ? "99+" : count.ToString();
            nav.Badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>The page shown now, if it's one of the social ones.</summary>
    public HubView? ShownView => _shownView;

    /// <summary>Shows each switch as its menu entry stands now.</summary>
    public void Refresh()
    {
        _sync();
        _profile?.Reload();
        foreach (var (item, toggle) in _rows)
        {
            toggle.Set(item.IsChecked, animate: false);
            if (item.Name == "MenuAnim") _animate = item.IsChecked;
        }
        RefreshApplyState();
        RefreshBadges();
    }

    void RefreshApplyState()
    {
        _apply.IsEnabled = HasChanges;
        _apply.Background = HasChanges ? HubTheme.Accent : HubTheme.Card;
    }

    Border NavItem(string key, string glyph, string title)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = HubTheme.Glyph(glyph, 15, HubTheme.Dim);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(icon);
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5 };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var badgeText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var badge = new Border
        {
            Background = HubTheme.Red, CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0),
            Child = badgeText, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);
        var item = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = grid,
        };
        item.MouseEnter += (_, _) => { if (Page != key) item.Background = HubTheme.Hover; };
        item.MouseLeave += (_, _) => { if (Page != key) item.Background = Brushes.Transparent; };
        item.MouseLeftButtonUp += (_, _) => Navigate(key);
        _navItems[key] = (item, icon, badge, badgeText);
        return item;
    }

    FrameworkElement GroupPage(SettingsGroup group)
    {
        var rows = new StackPanel();
        for (int i = 0; i < group.Switches.Count; i++)
        {
            if (i > 0) rows.Children.Add(new Border { Height = 1, Background = HubTheme.Line, Margin = new Thickness(58, 0, 0, 0) });
            rows.Children.Add(Row(group.Switches[i], group.Color, first: i == 0, last: i == group.Switches.Count - 1));
        }
        var card = new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Child = rows, VerticalAlignment = VerticalAlignment.Top };
        return HubTheme.Scroller(card);
    }

    FrameworkElement Row(SettingsSwitch setting, Color color, bool first, bool last)
    {
        var item = setting.Item;
        var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        toggle.Set(item.IsChecked, animate: false);
        _rows.Add((item, toggle));

        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, MinHeight = 56, ToolTip = item.ToolTip, IsEnabled = item.IsEnabled };
        var hover = new Border
        {
            CornerRadius = new CornerRadius(first ? 12 : 0, first ? 12 : 0, last ? 12 : 0, last ? 12 : 0),
        };
        row.Children.Add(hover);
        var content = new Grid { Margin = new Thickness(14, 10, 16, 10) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = HubTheme.IconTile(setting.Glyph, color);
        tile.Margin = new Thickness(0, 0, 14, 0);
        tile.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(tile);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = item.Header as string ?? "", FontSize = 14, TextWrapping = TextWrapping.Wrap });
        string? help = setting.Help ?? item.ToolTip as string;
        if (help != null)
            words.Children.Add(new TextBlock { Text = help, Foreground = HubTheme.Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(words, 1);
        content.Children.Add(words);
        Grid.SetColumn(toggle, 2);
        content.Children.Add(toggle);
        row.Children.Add(content);

        row.MouseEnter += (_, _) => hover.Background = HubTheme.Hover;
        row.MouseLeave += (_, _) => hover.Background = null;
        row.MouseLeftButtonUp += (_, _) => Flip(item, toggle);
        return row;
    }

    /// <summary>A question over the window, which blurs behind it; each choice closes it and runs.</summary>
    void ShowDialog(string title, string message, params (string Label, Action Run)[] choices)
    {
        _previousFocus = Keyboard.FocusedElement;
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = HubTheme.Dim, Margin = new Thickness(0, 10, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button? first = null;
        foreach (var (label, run) in choices)
        {
            var button = HubTheme.Action(label);
            button.Padding = new Thickness(12, 6, 12, 6);
            button.Margin = new Thickness(6, 0, 0, 0);
            button.Click += (_, _) => { HideDialog(); run(); };
            actions.Children.Add(button);
            first ??= button;
        }
        content.Children.Add(actions);
        _dialog.Child = new Border
        {
            Background = HubTheme.Frozen(Color.FromRgb(32, 32, 34)), BorderBrush = HubTheme.Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Width = 460,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 }, Child = content
        };
        _surface.Effect = new BlurEffect { Radius = 5 };
        _surface.IsEnabled = false;
        _dialog.Visibility = Visibility.Visible;
        first?.Focus();
    }

    void HideDialog()
    {
        _dialog.Visibility = Visibility.Collapsed;
        _dialog.Child = null;
        _surface.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Opacity = 0.5 };
        _surface.IsEnabled = true;
        if (_previousFocus != null) Keyboard.Focus(_previousFocus);
    }

    void Flip(MenuItem item, ToggleSwitch toggle)
    {
        bool next = !toggle.IsOn;
        if (item.Name == "MenuAnim") _animate = next;
        toggle.Set(next, _animate);
        if (!_animate) foreach (var row in _rows) row.Switch.Set(row.Switch.IsOn, false);
        RefreshApplyState();
    }

    void ApplyChanges()
    {
        try { _profile?.Apply(); }
        catch (Exception ex) { Diag.Error(ex); }
        foreach (var (item, toggle) in _rows)
        {
            if (item.IsChecked == toggle.IsOn) continue;
            item.IsChecked = toggle.IsOn;
            try { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); }
            catch (Exception ex) { Diag.Error(ex); }
        }
        Refresh();
    }

    void ResetDefaults()
    {
        var defaults = new AppSettings();
        foreach (var (item, toggle) in _rows)
        {
            if (!item.IsEnabled) continue;
            string name = item.Name switch { "MenuAnim" => "Animations", "MenuMini" => "MiniIdle", _ => item.Name.Substring(4) };
            bool value = name != "Autostart" && typeof(AppSettings).GetProperty(name)?.GetValue(defaults) is true;
            toggle.Set(value, false);
            if (name == "Animations") _animate = value;
        }
        RefreshApplyState();
    }
}

/// <summary>An on/off switch: a pill that turns green with the knob sliding to the right.</summary>
internal sealed class ToggleSwitch : FrameworkElement
{
    static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(ToggleSwitch), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly Color Off = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
    static readonly Color On = Color.FromRgb(0x30, 0xD1, 0x58);

    /// <summary>0 off, 1 on; in between while it slides.</summary>
    double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public bool IsOn { get; private set; }

    public void Set(bool on, bool animate)
    {
        IsOn = on;
        if (!animate)
        {
            BeginAnimation(PositionProperty, null);
            Position = on ? 1 : 0;
            return;
        }
        BeginAnimation(PositionProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    protected override Size MeasureOverride(Size availableSize) => new(40, 24);

    protected override void OnRender(DrawingContext dc)
    {
        double t = Math.Clamp(Position, 0, 1);
        var track = new SolidColorBrush(Color.FromArgb(
            (byte)(Off.A + (On.A - Off.A) * t), (byte)(Off.R + (On.R - Off.R) * t),
            (byte)(Off.G + (On.G - Off.G) * t), (byte)(Off.B + (On.B - Off.B) * t)));
        dc.DrawRoundedRectangle(track, null, new Rect(0, 0, 40, 24), 12, 12);
        dc.DrawEllipse(Brushes.White, null, new Point(12 + 16 * t, 12), 10, 10);
    }
}
