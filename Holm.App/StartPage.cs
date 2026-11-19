using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Settings › Start page: what the open notch shows, and where. Its sections from top to bottom, to drag (or move
/// with the arrows), hide, or swap side to side; the tools of its bar from left to right; and beside them a small
/// picture of the open notch as it will look. Each change applies at once and is saved (HomeLayout.cs). Until you
/// change something it is Holm's usual layout, and Reset to default brings that back.
/// </summary>
sealed class StartPage : HubView
{
    public const string Key = "start";

    /// <summary>A tool of the bar as the page shows it: its name, its icon in a colour and size, and whether its switch is on.</summary>
    public sealed record BarTool(string Name, string Title, Func<IBrush, double, Control> Icon,
                                 Func<bool>? Enabled = null, Action? Enable = null, string? DisabledHint = null)
    {
        public bool On => Enabled?.Invoke() ?? true;
    }

    const double PreviewWidth = 236;

    readonly Func<HomeLayout> _read;
    readonly Action<HomeLayout> _apply;
    readonly IReadOnlyList<BarTool> _tools;
    HomeLayout _layout;
    string _tool;

    readonly StackPanel _list = new();
    readonly Canvas _dropLayer = new() { IsHitTestVisible = false };
    readonly Border _dropLine = new() { Height = 2, CornerRadius = new CornerRadius(1), Background = HubTheme.Accent, IsVisible = false };
    readonly List<(string Section, Border Row)> _rows = new();
    readonly StackPanel _toolStrip = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _toolName = new() { FontSize = 12, Foreground = HubTheme.Dim, Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly CheckBox _showTool = new() { FontSize = 12, Margin = new Thickness(2, 4, 0, 0) };
    readonly Button _toolLeft, _toolRight, _reset;
    readonly Border _preview = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Dictionary<string, Border> _previewParts = new();

    // A section being dragged: which, its row, and where the pointer went down.
    (string Section, Border Row, double StartY)? _press;
    bool _dragging;
    int _dropIndex = -1;

    public StartPage(Func<HomeLayout> read, Action<HomeLayout> apply, IReadOnlyList<BarTool> tools, bool focumon, Action<bool> setFocumon)
    {
        _read = read;
        _apply = apply;
        _tools = tools;
        _layout = read();
        _tool = tools.Count > 0 ? tools[0].Name : HomeLayout.Notes;

        // ── the sections ──
        var left = new StackPanel();
        left.Children.Add(HubTheme.Caption("Sections, top to bottom"));
        _dropLayer.Children.Add(_dropLine);
        var listArea = new Grid { Children = { _list, _dropLayer } };
        left.Children.Add(Card(listArea));
        left.Children.Add(Note("Drag a section to move it, or use its arrows. The eye hides a section, and the double arrow swaps its left and right side."));
        _list.PointerMoved += (_, e) => DragTo(e);
        _list.PointerReleased += (_, e) => Drop(e);
        _list.PointerCaptureLost += (_, _) => EndDrag();

        // ── the tools ──
        left.Children.Add(new Border { Height = 18 });
        left.Children.Add(HubTheme.Caption("Tool bar, left to right"));
        _toolLeft = IconButton(SideChevron(left: true), "Move left", () => MoveTool(-1));
        _toolRight = IconButton(SideChevron(left: false), "Move right", () => MoveTool(1));
        var toolRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toolRow.Children.Add(_toolStrip);
        var moves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        moves.Children.Add(_toolLeft);
        moves.Children.Add(_toolRight);
        Grid.SetColumn(moves, 1);
        toolRow.Children.Add(moves);
        var toolCard = new StackPanel { Margin = Look.Pick(new Thickness(2, 10, 2, 10), new Thickness(12, 12, 12, 10)) };
        toolCard.Children.Add(toolRow);
        toolCard.Children.Add(_showTool);
        toolCard.Children.Add(_toolName);
        _showTool.Click += (_, _) =>
        {
            bool show = _showTool.IsChecked == true;
            if (show && _tools.FirstOrDefault(tool => tool.Name == _tool) is { On: false } tool) tool.Enable?.Invoke();
            Change(layout => layout.HideTool(_tool, !show));
        };
        left.Children.Add(Card(toolCard));
        var showFocumon = new CheckBox { Content = "Show Focumon", IsChecked = focumon, Margin = new Thickness(4, 10, 0, 0) };
        showFocumon.Click += (_, _) => setFocumon(showFocumon.IsChecked == true);
        left.Children.Add(showFocumon);
        left.Children.Add(Note("Show your monster and timer in the open toolbar and the closed notch. Requires the Focumon for Holm browser extension."));

        // ── the picture ──
        var right = new StackPanel { Width = PreviewWidth, VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(HubTheme.Caption("Preview"));
        right.Children.Add(_preview);
        right.Children.Add(Note("Without music. The activity and the shelf show only while there is something in them."));
        _reset = HubTheme.Action("Reset to default");
        _reset.Margin = new Thickness(0, 14, 0, 0);
        _reset.Click += (_, _) => Change(_ => HomeLayout.Usual);
        right.Children.Add(_reset);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,Auto") };
        columns.Children.Add(left);
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);
        Child = HubTheme.Scroller(columns);
        Refresh();
    }

    /// <summary>What each section is called, says, and looks like in the list.</summary>
    static (string Title, string Help, string Glyph, Color Color) Describe(string section) => section switch
    {
        HomeLayout.Tools => ("Tool bar", "Notes, reminders, calculator and more, with the sleep timer", "", Color.FromRgb(0x5E, 0x5C, 0xE6)),
        HomeLayout.Main => ("Player and clock", "The music while it plays, otherwise the time, date and weather", "", Color.FromRgb(0xFF, 0x37, 0x5F)),
        HomeLayout.Today => ("Reminder and note", "Two cards, while no music plays", "", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        HomeLayout.Live => ("Activity", "Messages, downloads, drives, calls and the timer, when there are any", "", Color.FromRgb(0x34, 0xC7, 0x59)),
        HomeLayout.Shelf => ("Shelf", "Files parked on the notch, when there are any", "", Color.FromRgb(0x8E, 0x8E, 0x93)),
        _ => ("Battery and volume", "Batteries, Bluetooth, sound output and the volume", "", Color.FromRgb(0x0A, 0x84, 0xFF)),
    };

    static string SwapTip(string section) => section switch
    {
        HomeLayout.Main => "Swap sides: the weather left of the clock, the cover right of the title",
        HomeLayout.Today => "Swap sides: the note first",
        _ => "Swap sides: the volume on the left",
    };

    /// <summary>Applies a change at once: from what's saved now, so nothing that changed meanwhile is lost.</summary>
    void Change(Func<HomeLayout, HomeLayout> edit)
    {
        _layout = edit(_read());
        _apply(_layout);
        Refresh();
    }

    void Refresh()
    {
        FillSections();
        FillTools();
        _preview.Child = Preview();
        _reset.IsEnabled = !_layout.IsUsual;
    }

    // ───────────── sections ─────────────

    void FillSections()
    {
        EndDrag();
        _rows.Clear();
        _list.Children.Clear();
        var order = _layout.Sections;
        for (int i = 0; i < order.Count; i++)
        {
            if (Look.Instrument || i > 0)
                _list.Children.Add(new Border { Height = 1, Background = HubTheme.Line, Margin = new Thickness(Look.Pick(0, 69), 0, 0, 0), IsHitTestVisible = false });
            var row = SectionRow(order[i], i, order.Count);
            _rows.Add((order[i], row));
            _list.Children.Add(row);
        }
    }

    Border SectionRow(string section, int index, int count)
    {
        var (title, help, glyph, color) = Describe(section);
        bool hidden = _layout.IsHidden(section);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), MinHeight = 40,
            Margin = Look.Pick(new Thickness(2, 9, 2, 9), new Thickness(10, 9, 10, 9)),
        };
        grid.Children.Add(new Path
        {
            Data = Geometry.Parse("M0,0h2.4v2.4h-2.4z M4.6,0h2.4v2.4h-2.4z M0,4.6h2.4v2.4h-2.4z M4.6,4.6h2.4v2.4h-2.4z M0,9.2h2.4v2.4h-2.4z M4.6,9.2h2.4v2.4h-2.4z"),
            Fill = HubTheme.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        });
        if (!Look.Instrument) // the instrument look has no icon in front of a row
        {
            var tile = HubTheme.IconTile(glyph, color);
            tile.Margin = new Thickness(0, 0, 12, 0);
            tile.VerticalAlignment = VerticalAlignment.Center;
            tile.Opacity = hidden ? 0.45 : 1;
            Grid.SetColumn(tile, 1);
            grid.Children.Add(tile);
        }
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Opacity = hidden ? 0.45 : 1 };
        words.Children.Add(new TextBlock { Text = hidden ? title + " (hidden)" : title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(new TextBlock { Text = help, FontSize = 12, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(words, 2);
        grid.Children.Add(words);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        if (HomeLayout.CanSwap.Contains(section))
        {
            bool swapped = _layout.IsSwapped(section);
            var swap = IconButton(SwapIcon(swapped ? HubTheme.Accent : HubTheme.Dim), SwapTip(section), () => Change(l => l.Swap(section, !swapped)));
            if (swapped) Lit(swap);
            actions.Children.Add(swap);
        }
        else actions.Children.Add(Gap());
        if (HomeLayout.CanHide.Contains(section))
            actions.Children.Add(IconButton(HubTheme.Glyph(hidden ? "" : "", 14, hidden ? HubTheme.Dim : Look.Ink),
                hidden ? "Show" : "Hide", () => Change(l => l.Hide(section, !hidden))));
        else actions.Children.Add(Gap());
        var up = IconButton(UpDownChevron(up: true), "Move up", () => Change(l => l.Move(section, index - 1)));
        up.IsEnabled = index > 0;
        var down = IconButton(UpDownChevron(up: false), "Move down", () => Change(l => l.Move(section, index + 1)));
        down.IsEnabled = index < count - 1;
        actions.Children.Add(up);
        actions.Children.Add(down);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        var row = new Border
        {
            Background = Brushes.Transparent, Child = grid, Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            CornerRadius = new CornerRadius(Look.Instrument ? 0 : HubTheme.Round(12)),
        };
        row.PointerEntered += (_, _) => { row.Background = HubTheme.Hover; Point(section, true); };
        row.PointerExited += (_, _) => { if (_press?.Row != row) row.Background = Brushes.Transparent; Point(section, false); };
        // Presses on its buttons are theirs; the rest of the row picks the section up.
        row.AddHandler(PointerPressedEvent, (_, e) => PickUp(section, row, e), RoutingStrategies.Bubble);
        return row;
    }

    void PickUp(string section, Border row, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
        _press = (section, row, e.GetPosition(_list).Y);
        _dragging = false;
        _dropIndex = -1;
        // The list keeps the pointer, so the drag goes on wherever it moves.
        e.Pointer.Capture(_list);
        e.Handled = true;
    }

    void DragTo(PointerEventArgs e)
    {
        if (_press is not { } press) return;
        double y = e.GetPosition(_list).Y;
        if (!_dragging)
        {
            if (Math.Abs(y - press.StartY) < 4) return;
            _dragging = true;
            press.Row.Opacity = 0.7;
            press.Row.ZIndex = 1;
            press.Row.RenderTransform = new TranslateTransform();
        }
        if (press.Row.RenderTransform is TranslateTransform move) move.Y = y - press.StartY;

        // Where it would land: before the first other row whose middle is under the pointer.
        var others = _rows.Where(r => r.Section != press.Section).Select(r => r.Row).ToList();
        _dropIndex = others.Count(r => r.Bounds.Y + r.Bounds.Height / 2 < y);
        int from = _rows.FindIndex(r => r.Section == press.Section);
        _dropLine.IsVisible = _dropIndex != from && others.Count > 0;
        if (!_dropLine.IsVisible) return;
        double lineY = _dropIndex < others.Count ? others[_dropIndex].Bounds.Top : others[^1].Bounds.Bottom;
        _dropLine.Width = Math.Max(0, _list.Bounds.Width - 16);
        Canvas.SetLeft(_dropLine, 8);
        Canvas.SetTop(_dropLine, lineY - 1);
    }

    void Drop(PointerReleasedEventArgs e)
    {
        if (_press is not { } press) return;
        bool moved = _dragging && _dropIndex >= 0 && _dropIndex != _rows.FindIndex(r => r.Section == press.Section);
        int index = _dropIndex;
        EndDrag();
        e.Pointer.Capture(null);
        if (moved) Change(l => l.Move(press.Section, index));
    }

    void EndDrag()
    {
        if (_press is { } press)
        {
            press.Row.Opacity = 1;
            press.Row.ZIndex = 0;
            press.Row.RenderTransform = null;
            if (!press.Row.IsPointerOver) press.Row.Background = Brushes.Transparent;
        }
        _press = null;
        _dragging = false;
        _dropIndex = -1;
        _dropLine.IsVisible = false;
    }

    // ───────────── tools ─────────────

    void FillTools()
    {
        _toolStrip.Children.Clear();
        var order = _layout.ToolOrder;
        foreach (string name in order)
        {
            if (_tools.FirstOrDefault(t => t.Name == name) is not { } tool) continue;
            bool picked = name == _tool;
            bool hidden = _layout.IsToolHidden(name);
            var chip = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(Look.Pick(2, HubTheme.Round(9))), Cursor = new Cursor(StandardCursorType.Hand),
                Background = picked ? HubTheme.Selected : Brushes.Transparent, BorderBrush = picked ? HubTheme.Accent : HubTheme.Line,
                BorderThickness = new Thickness(picked ? 1.5 : 1), Opacity = tool.On && !hidden ? 1 : 0.4,
                Child = Center(tool.Icon(picked ? Look.Ink : HubTheme.Dim, 15)),
            };
            ToolTip.SetTip(chip, hidden ? tool.Title + " (hidden)" : tool.On ? tool.Title : tool.Title + " (switched off)");
            chip.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                _tool = name;
                Refresh();
            };
            _toolStrip.Children.Add(chip);
        }
        int at = order.ToList().IndexOf(_tool);
        _toolLeft.IsEnabled = at > 0;
        _toolRight.IsEnabled = at >= 0 && at < order.Count - 1;
        var chosen = _tools.FirstOrDefault(t => t.Name == _tool);
        _showTool.IsEnabled = chosen is { On: true } || chosen?.Enable != null;
        _showTool.IsChecked = chosen is { On: true } && !_layout.IsToolHidden(_tool);
        _showTool.Content = chosen == null ? "Show in tool bar" : $"Show {chosen.Title} in tool bar";
        _toolName.Text = chosen is { On: false } ? chosen.DisabledHint : null;
        _toolName.IsVisible = _toolName.Text != null;
    }

    void MoveTool(int step)
    {
        int at = _layout.ToolOrder.ToList().IndexOf(_tool);
        if (at < 0) return;
        Change(l => l.MoveTool(_tool, at + step));
    }

    // ───────────── the picture ─────────────

    /// <summary>The open notch drawn small: each section as it would sit, the hidden ones left out.</summary>
    Control Preview()
    {
        _previewParts.Clear();
        var stack = new StackPanel();
        var shown = _layout.Sections.Where(s => !_layout.IsHidden(s)).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            string section = shown[i];
            var part = PreviewPart(section, above: i > 0, below: i < shown.Count - 1);
            var frame = new Border
            {
                Child = part, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
                Padding = new Thickness(3, 2), Margin = new Thickness(-4, i == 0 || section == HomeLayout.Status || shown[i - 1] == HomeLayout.Status ? 0 : 5, -4, 0),
            };
            _previewParts[section] = frame;
            stack.Children.Add(frame);
        }
        double round = Look.Pick(4, 22);
        return new Border
        {
            Width = PreviewWidth, Background = Look.NotchFill(), CornerRadius = new CornerRadius(0, 0, round, round), ClipToBounds = true,
            BorderBrush = Look.Current.Edge != 0 ? Look.Brush(Look.Current.Edge) : HubTheme.Edge, BorderThickness = new Thickness(1, 0, 1, 1),
            Padding = new Thickness(16, 12, 16, 14), Child = stack,
        };
    }

    /// <summary>A section of the picture lit while the pointer is on its row.</summary>
    void Point(string section, bool on)
    {
        if (_previewParts.TryGetValue(section, out var frame)) frame.BorderBrush = on ? HubTheme.Accent : Brushes.Transparent;
    }

    Control PreviewPart(string section, bool above, bool below)
    {
        IBrush ink = Look.Ink, dim = Look.InkA(0x99), faint = Look.InkA(0x40), fill = Look.InkA(0x1A);
        bool swapped = _layout.IsSwapped(section);
        switch (section)
        {
            case HomeLayout.Tools:
            {
                var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = Look.Pick(HorizontalAlignment.Right, HorizontalAlignment.Center), Margin = Look.Pick(new Thickness(0, 0, 16, 0), new Thickness(0)) };
                foreach (string name in _layout.ToolOrder)
                    if (!_layout.IsToolHidden(name) && _tools.FirstOrDefault(t => t.Name == name) is { On: true } tool)
                        icons.Children.Add(tool.Icon(dim, 9));
                var row = new Grid { Height = 16 };
                row.Children.Add(icons);
                row.Children.Add(new TextBlock { Text = "", FontFamily = Fonts.Icons, FontSize = 8, Foreground = faint, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
                foreach (var icon in icons.Children) icon.VerticalAlignment = VerticalAlignment.Center;
                return row;
            }
            case HomeLayout.Main:
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(swapped ? "Auto,*" : "*,Auto") };
                var clock = new StackPanel { HorizontalAlignment = swapped ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                clock.Children.Add(new TextBlock { Text = "09:41", FontFamily = Fonts.Display, FontSize = 20, FontWeight = FontWeight.Light, Foreground = ink, HorizontalAlignment = clock.HorizontalAlignment });
                clock.Children.Add(Bar(58, 4, faint, clock.HorizontalAlignment, new Thickness(0, 2, 0, 0)));
                Grid.SetColumn(clock, swapped ? 1 : 0);
                var weather = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = swapped ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                weather.Children.Add(new TextBlock { Text = "", FontFamily = Fonts.Icons, FontSize = 11, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
                weather.Children.Add(new TextBlock { Text = "18°", FontSize = 12, FontWeight = FontWeight.Light, Foreground = ink, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(weather, swapped ? 0 : 1);
                grid.Children.Add(clock);
                grid.Children.Add(weather);
                return grid;
            }
            case HomeLayout.Today:
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,*") };
                var reminder = MiniCard("", dim, fill, faint);
                var note = MiniCard("", dim, fill, faint);
                Grid.SetColumn(swapped ? reminder : note, 2);
                grid.Children.Add(reminder);
                grid.Children.Add(note);
                return grid;
            }
            case HomeLayout.Live:
            {
                var row = MiniCard("", dim, Look.InkA(0x10), faint);
                row.Opacity = 0.6;
                return row;
            }
            case HomeLayout.Shelf:
            {
                var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Opacity = 0.6 };
                for (int i = 0; i < 3; i++) chips.Children.Add(new Border { Width = 30, Height = 12, CornerRadius = new CornerRadius(Look.Pick(1, 4)), Background = fill });
                return chips;
            }
            default:
            {
                var stack = new StackPanel();
                if (above) stack.Children.Add(new Border { Height = 1, Background = fill, Margin = new Thickness(0, 6, 0, 5) });
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(swapped ? "Auto,*" : "*,Auto"), Height = 14 };
                var battery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = swapped ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                battery.Children.Add(new Border
                {
                    Width = 14, Height = 7, CornerRadius = new CornerRadius(1.5), BorderBrush = dim, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
                    Child = new Border { Width = 8, Background = dim, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1) },
                });
                battery.Children.Add(new TextBlock { Text = "85%", FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(battery, swapped ? 1 : 0);
                var volume = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                volume.Children.Add(new TextBlock { Text = "", FontFamily = Fonts.Icons, FontSize = 9, Foreground = dim, VerticalAlignment = VerticalAlignment.Center });
                volume.Children.Add(new Border
                {
                    Width = 44, Height = 3, CornerRadius = new CornerRadius(1.5), Background = fill, VerticalAlignment = VerticalAlignment.Center,
                    Child = new Border { Width = 22, Background = Look.Accent, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left },
                });
                Grid.SetColumn(volume, swapped ? 0 : 1);
                grid.Children.Add(battery);
                grid.Children.Add(volume);
                stack.Children.Add(grid);
                if (below) stack.Children.Add(new Border { Height = 1, Background = fill, Margin = new Thickness(0, 5, 0, 6) });
                return stack;
            }
        }
    }

    static Border MiniCard(string glyph, IBrush ink, IBrush fill, IBrush faint)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Fonts.Icons, FontSize = 8, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(Bar(40, 4, faint, HorizontalAlignment.Left, default));
        return new Border { Height = 20, CornerRadius = new CornerRadius(Look.Pick(1, 6)), Background = fill, Padding = new Thickness(7, 0), Child = content };
    }

    static Border Bar(double width, double height, IBrush brush, HorizontalAlignment align, Thickness margin) => new()
    {
        Width = width, Height = height, CornerRadius = new CornerRadius(height / 2), Background = brush, HorizontalAlignment = align,
        VerticalAlignment = VerticalAlignment.Center, Margin = margin,
    };

    // ───────────── pieces ─────────────

    static Border Card(Control content) => Look.Instrument
        ? new Border { Child = content }
        : new Border
        {
            Background = HubTheme.Card, BorderBrush = Look.Aero ? HubTheme.Line : null, BorderThickness = new Thickness(Look.Aero ? 1 : 0),
            CornerRadius = new CornerRadius(HubTheme.Round(12)), Child = content,
        };

    static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 0, 0),
    };

    static Control Center(Control content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        return content;
    }

    static Border Gap() => new() { Width = 28, Height = 28 };

    /// <summary>A small square button with an icon, softly lit under the mouse.</summary>
    static Button IconButton(Control icon, string tip, Action click)
    {
        var button = new Button
        {
            Content = icon, Width = 28, Height = 28, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(Look.Pick(2, HubTheme.Round(7))), Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Focusable = false,
        };
        // The theme repaints a button under the mouse; ours takes the window's soft hover.
        button.Resources["ButtonBackgroundPointerOver"] = HubTheme.Hover;
        button.Resources["ButtonBackgroundPressed"] = HubTheme.Selected;
        button.Resources["ButtonBackgroundDisabled"] = Brushes.Transparent;
        foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" }) button.Resources["ButtonBorderBrush" + state] = Brushes.Transparent;
        button.PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) button.Opacity = button.IsEnabled ? 1 : 0.3; };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A swapped section's button stays lit.</summary>
    static void Lit(Button button)
    {
        button.Background = HubTheme.Selected;
        button.Resources["ButtonBackgroundPointerOver"] = HubTheme.Selected;
    }

    static Path UpDownChevron(bool up) => new()
    {
        Data = Geometry.Parse(up ? "M1,7 L6,2 L11,7" : "M1,2 L6,7 L11,2"), Stroke = Look.Ink, StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 12, Height = 9,
    };

    static Path SideChevron(bool left) => new()
    {
        Data = Geometry.Parse(left ? "M7,1 L2,6 L7,11" : "M2,1 L7,6 L2,11"), Stroke = Look.Ink, StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 9, Height = 12,
    };

    static Path SwapIcon(IBrush brush) => new()
    {
        Data = Geometry.Parse("M2,5 H14 M11,2 L14,5 L11,8 M14,11 H2 M5,8 L2,11 L5,14"), Stroke = brush, StrokeThickness = 1.5,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 16, Height = 16,
    };
}
