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

/// <summary>A group of switches in the settings window, with the menu entries that hold their handlers.</summary>
internal sealed record SettingsGroup(string Title, IReadOnlyList<MenuItem> Switches);

/// <summary>
/// The on/off switches that used to make the right-click menu taller than the screen, in a window of their own.
/// Switches stage a draft; Apply raises the hidden menu entries' handlers to save the changes.
/// </summary>
internal sealed class SettingsWindow : Window
{
    static readonly Brush Panel = Frozen(Color.FromArgb(0xF7, 0x20, 0x20, 0x22));
    static readonly Brush Edge = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    static readonly Brush Card = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    static readonly Brush Hover = Frozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
    static readonly Brush Line = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

    bool _animate;
    bool HasChanges => _rows.Any(r => r.Item.IsChecked != r.Switch.IsOn);
    readonly Action _sync;
    readonly List<(MenuItem Item, ToggleSwitch Switch)> _rows = new();

    /// <param name="sync">Checks the menu entries as the settings stand, before the switches are shown.</param>
    public SettingsWindow(IReadOnlyList<SettingsGroup> groups, string version, bool animate, Action sync)
    {
        _animate = animate;
        _sync = sync;
        sync();
        Title = "Holm settings";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; // the notch is topmost too; this must not open behind it
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        Foreground = Brushes.White;

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);

        // Fill the shorter column, so the two end at about the same height.
        int leftRows = 0, rightRows = 0;
        foreach (var group in groups)
        {
            bool toLeft = leftRows <= rightRows;
            (toLeft ? left : right).Children.Add(Group(group));
            if (toLeft) leftRows += group.Switches.Count + 2; else rightRows += group.Switches.Count + 2;
        }

        var body = new DockPanel();
        var header = Header(version);
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var actionStyle = ActionButtonStyle();
        var reset = new Button { Style = actionStyle, Content = "Reset to defaults", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 10, 0) };
        var apply = new Button { Style = actionStyle, Background = new SolidColorBrush(Color.FromRgb(10, 132, 255)), BorderBrush = Brushes.Transparent, Content = "Apply", Padding = new Thickness(18, 6, 18, 6) };
        reset.Click += (_, _) => ResetDefaults();
        apply.Click += (_, _) => ApplyChanges();
        footer.Children.Add(reset);
        footer.Children.Add(apply);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);
        body.Children.Add(columns);

        Content = new Border
        {
            Background = Panel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 16, 22, 22),
            Margin = new Thickness(16), // room for the shadow
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 },
            Child = body,
        };

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Activated += (_, _) => { if (!HasChanges) Refresh(); };
        Closing += (_, e) =>
        {
            if (!HasChanges) return;
            var choice = MessageBox.Show(this, "Your changes have not been applied. Apply them before closing?\n\nYes: Apply and close\nNo: Discard and close\nCancel: Keep editing", "Unapplied changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Cancel) e.Cancel = true;
            else if (choice == MessageBoxResult.Yes) { ApplyChanges(); e.Cancel = HasChanges; }
        };
    }

    /// <summary>Shows each switch as its menu entry stands now.</summary>
    public void Refresh()
    {
        _sync();
        foreach (var (item, toggle) in _rows)
        {
            toggle.Set(item.IsChecked, animate: false);
            if (item.Name == "MenuAnim") _animate = item.IsChecked;
        }
    }

    static Style ActionButtonStyle() => (Style)System.Windows.Markup.XamlReader.Parse(@"
        <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
               xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
            <Setter Property='Foreground' Value='White'/>
            <Setter Property='Background' Value='#14FFFFFF'/>
            <Setter Property='BorderBrush' Value='#24FFFFFF'/>
            <Setter Property='FontSize' Value='12'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='MinHeight' Value='32'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Surface' Background='{TemplateBinding Background}'
                                BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7'
                                Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Surface' Property='Opacity' Value='0.82'/>
                            </Trigger>
                            <Trigger Property='IsPressed' Value='True'>
                                <Setter TargetName='Surface' Property='Opacity' Value='0.65'/>
                            </Trigger>
                            <Trigger Property='IsKeyboardFocused' Value='True'>
                                <Setter TargetName='Surface' Property='BorderBrush' Value='#B3FFFFFF'/>
                            </Trigger>
                            <Trigger Property='IsEnabled' Value='False'>
                                <Setter TargetName='Surface' Property='Opacity' Value='0.4'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>");

    FrameworkElement Header(string version)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Settings", FontSize = 20, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Holm " + version, Foreground = Dim, FontSize = 12, Margin = new Thickness(0, 1, 0, 0) });
        header.Children.Add(titles);

        var close = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Card, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Close (Esc)",
            Child = new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        close.MouseEnter += (_, _) => close.Background = Edge;
        close.MouseLeave += (_, _) => close.Background = Card;
        close.MouseLeftButtonDown += (_, e) => e.Handled = true; // not the start of a drag
        close.MouseLeftButtonUp += (_, _) => Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        return header;
    }

    FrameworkElement Group(SettingsGroup group)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = group.Title.ToUpperInvariant(), Foreground = Dim, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 6),
        });
        var rows = new StackPanel();
        for (int i = 0; i < group.Switches.Count; i++)
        {
            if (i > 0) rows.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(12, 0, 0, 0) });
            rows.Children.Add(Row(group.Switches[i], first: i == 0, last: i == group.Switches.Count - 1));
        }
        panel.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(10), Child = rows, ClipToBounds = true });
        return panel;
    }

    FrameworkElement Row(MenuItem item, bool first, bool last)
    {
        var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        toggle.Set(item.IsChecked, animate: false);
        _rows.Add((item, toggle));

        var row = new Grid
        {
            Background = Brushes.Transparent, Cursor = Cursors.Hand, MinHeight = 38,
        };
        var hover = new Border
        {
            CornerRadius = new CornerRadius(first ? 10 : 0, first ? 10 : 0, last ? 10 : 0, last ? 10 : 0),
        };
        row.Children.Add(hover);
        var content = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock
        {
            Text = item.Header as string ?? "", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8),
        });
        Grid.SetColumn(toggle, 1);
        content.Children.Add(toggle);
        row.Children.Add(content);

        row.MouseEnter += (_, _) => hover.Background = Hover;
        row.MouseLeave += (_, _) => hover.Background = null;
        row.MouseLeftButtonUp += (_, _) => Flip(item, toggle);
        return row;
    }

    void Flip(MenuItem item, ToggleSwitch toggle)
    {
        bool next = !toggle.IsOn;
        if (item.Name == "MenuAnim") _animate = next;
        toggle.Set(next, _animate);
        if (!_animate) foreach (var row in _rows) row.Switch.Set(row.Switch.IsOn, false);
    }

    void ApplyChanges()
    {
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
            string name = item.Name switch { "MenuAnim" => "Animations", "MenuMini" => "MiniIdle", _ => item.Name.Substring(4) };
            bool value = name != "Autostart" && typeof(AppSettings).GetProperty(name)?.GetValue(defaults) is true;
            toggle.Set(value, false);
            if (name == "Animations") _animate = value;
        }
    }

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
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

    protected override Size MeasureOverride(Size availableSize) => new(38, 22);

    protected override void OnRender(DrawingContext dc)
    {
        double t = Math.Clamp(Position, 0, 1);
        var track = new SolidColorBrush(Color.FromArgb(
            (byte)(Off.A + (On.A - Off.A) * t), (byte)(Off.R + (On.R - Off.R) * t),
            (byte)(Off.G + (On.G - Off.G) * t), (byte)(Off.B + (On.B - Off.B) * t)));
        dc.DrawRoundedRectangle(track, null, new Rect(0, 0, 38, 22), 11, 11);
        dc.DrawEllipse(Brushes.White, null, new Point(11 + 16 * t, 11), 9, 9);
    }
}
