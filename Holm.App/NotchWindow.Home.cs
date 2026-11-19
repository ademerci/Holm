using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The open notch's start page (home) in the sections you arranged under Settings › Start page (HomeLayout.cs): the
/// tool bar, the player or clock, the two cards, the activity (messages, downloads, drives, calls, the timer), the
/// shelf, and the battery and volume. Each section is a panel of its own in the open notch, in your order; a tool's
/// page takes the place of the player and everything else of home while it is open. Unless you change it, the
/// layout is Holm's usual one.
/// </summary>
public sealed partial class NotchWindow
{
    HomeLayout _home = HomeLayout.Usual;

    Control _toolRow = null!;
    readonly StackPanel _mainSection = new(), _todaySection = new(), _liveSection = new(), _shelfSection = new(), _statusSection = new();
    // The line between the battery and volume and the rest, on each side something shows.
    readonly Border _statusLineAbove = new() { Height = 1, Background = Look.InkA(0x1A), Margin = new Thickness(0, 12, 0, 8) };
    readonly Border _statusLineBelow = new() { Height = 1, Background = Look.InkA(0x1A), Margin = new Thickness(0, 8, 0, 12), IsVisible = false };
    // What swaps sides: the cover and the title, the clock and the weather, the batteries and the volume.
    Grid _clockHeader = null!, _statusRow = null!;
    StackPanel _playerTexts = null!, _clockStack = null!, _soundPanel = null!;
    bool _devicesSwapped;

    /// <summary>Settings › Start page, where you arrange it; each change is saved and shows the next time the notch opens.</summary>
    HubView BuildStartPage() => new StartPage(() => HomeLayout.Read(_settings), layout =>
    {
        layout.Write(_settings);
        _settings.Save();
        ApplyHomeLayout();
    }, new StartPage.BarTool[]
    {
        new(HomeLayout.Notes, "Notes", (brush, size) => ToolIcon("", brush, size), _settings.Notes),
        new(HomeLayout.Reminders, "Reminders", (brush, size) => ToolIcon(BellGlyph, brush, size), _settings.Reminders),
        new(HomeLayout.Calculator, "Calculator", (brush, size) => ToolIcon("", brush, size), _settings.Calculator),
        new(HomeLayout.Chats, "Chats", (brush, size) => ToolIcon(ChatsGlyph, brush, size), true),
        new(HomeLayout.Clipboard, "Clipboard history", (brush, size) => ToolIcon(ClipboardGlyph, brush, size), _settings.ClipboardHistory),
        new(HomeLayout.Mixer, "Volume per app", (brush, size) => ToolIcon(MixerGlyph, brush, size), _settings.AppVolume),
        // Its button shows only while Claude Code or Codex has been used on this computer.
        new(HomeLayout.Ai, "AI usage", AiUsageIcon, _settings.AiUsage),
        new(HomeLayout.ColorPicker, "Color picker", (brush, size) => ToolIcon(EyedropperGlyph, brush, size), _settings.ColorPicker),
    });

    static TextBlock ToolIcon(string glyph, IBrush brush, double size) => new() { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = brush };

    /// <summary>Reads the layout from the settings and lays the start page out that way.</summary>
    void ApplyHomeLayout()
    {
        _home = HomeLayout.Read(_settings);
        ArrangeHome();
        ArrangeToolBar();
        RefreshToolBar();
        SwapSides();
        ShowHomeSections();
        if (_state == NotchState.Expanded) ApplyState(animate: true);
    }

    Control Section(string section) => section switch
    {
        HomeLayout.Tools => _toolRow,
        HomeLayout.Main => _mainSection,
        HomeLayout.Today => _todaySection,
        HomeLayout.Live => _liveSection,
        HomeLayout.Shelf => _shelfSection,
        _ => _statusSection,
    };

    /// <summary>The sections of home a tool's page stands in for.</summary>
    IEnumerable<StackPanel> HomeContent() => new[] { _mainSection, _todaySection, _liveSection, _shelfSection };

    /// <summary>The sections into the open notch in their order, with a tool's page where the player and clock are.</summary>
    void ArrangeHome()
    {
        var parts = new List<Control>();
        foreach (string section in _home.Sections)
        {
            if (section == HomeLayout.Main) parts.Add(_toolPage);
            parts.Add(Section(section));
        }
        if (_expandedPanel.Children.SequenceEqual(parts)) return;
        _expandedPanel.Children.Clear();
        _expandedPanel.Children.AddRange(parts);
    }

    /// <summary>The tools in your order; anything else in the bar stays after them.</summary>
    void ArrangeToolBar()
    {
        var buttons = new Dictionary<string, Control>
        {
            [HomeLayout.Notes] = _notesToolBtn, [HomeLayout.Reminders] = _remindersToolBtn, [HomeLayout.Calculator] = _calculatorToolBtn,
            [HomeLayout.Chats] = _chatsToolBtn, [HomeLayout.Clipboard] = _clipboardToolBtn, [HomeLayout.Mixer] = _mixerToolBtn,
            [HomeLayout.Ai] = _aiToolBtn, [HomeLayout.ColorPicker] = _colorPickerBtn,
        };
        var wanted = _home.ToolOrder.Select(tool => buttons[tool]).ToList();
        wanted.AddRange(_toolBar.Children.Where(child => !wanted.Contains(child)).ToList());
        if (_toolBar.Children.SequenceEqual(wanted)) return;
        _toolBar.Children.Clear();
        _toolBar.Children.AddRange(wanted);
    }

    /// <summary>Which sections show: home's while no tool is open, and the bar while one is, even if you hid it, to go back.</summary>
    void ShowHomeSections()
    {
        bool home = _tool == Tool.None;
        _toolRow.IsVisible = home ? !_home.IsHidden(HomeLayout.Tools) : true;
        _mainSection.IsVisible = home;
        // The cards belong to the clock: while music plays they make way, as before.
        _todaySection.IsVisible = home && _clockPanel.IsVisible && !_home.IsHidden(HomeLayout.Today);
        _liveSection.IsVisible = home;
        _shelfSection.IsVisible = home;
        _statusSection.IsVisible = !_home.IsHidden(HomeLayout.Status);
    }

    /// <summary>
    /// The gaps between the sections for the order they're in, before the open notch is measured. The bar keeps 12 px
    /// to what's beside it and the line its own; a row brings its gap above it, except at the top, under the bar or
    /// under the line. In the usual order this is exactly the spacing home always had.
    /// </summary>
    void SpaceHome()
    {
        ShowHomeSections();
        var shown = _expandedPanel.Children.Where(Shows).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            var part = shown[i];
            var above = i > 0 ? shown[i - 1] : null;
            var below = i + 1 < shown.Count ? shown[i + 1] : null;
            bool fresh = above == null || above == _toolRow || above == _statusSection; // the gap above is there already
            if (part == _toolRow)
                _toolRow.Margin = new Thickness(0, above != null && above != _statusSection ? 12 : 0, 0, below != null && below != _statusSection ? 12 : 0);
            else if (part == _statusSection)
            {
                _statusLineAbove.IsVisible = above != null;
                _statusLineBelow.IsVisible = below != null;
            }
            else if (part == _mainSection) _mainSection.Margin = new Thickness(0, fresh ? 0 : 12, 0, 0);
            else if (part is StackPanel section && part != _toolPage) section.Margin = new Thickness(0, fresh ? -Lead(section) : 0, 0, 0);
        }
    }

    /// <summary>A part shows when it is visible and, for a section, when something in it is.</summary>
    bool Shows(Control part)
        => part.IsVisible && (part is not StackPanel section || part == _statusSection || section.Children.Any(child => child.IsVisible));

    /// <summary>The gap a section's first row keeps above itself, through the panels it sits in.</summary>
    static double Lead(Panel section)
    {
        double gap = 0;
        for (Control? row = section.Children.FirstOrDefault(child => child.IsVisible); row != null;
             row = row is StackPanel { Orientation: Orientation.Vertical } inner ? inner.Children.FirstOrDefault(child => child.IsVisible) : null)
            gap += row.Margin.Top;
        return gap;
    }

    /// <summary>Puts each section's two sides where you want them.</summary>
    void SwapSides()
    {
        // The player: the cover on the right of the title. The clock: the weather on the left, the time on the right.
        bool main = _home.IsSwapped(HomeLayout.Main);
        Grid.SetColumn(_bigArt, main ? 2 : 0);
        _playerTexts.Margin = main ? new Thickness(0, 0, 14, 0) : new Thickness(14, 0, 0, 0);
        _clockHeader.ColumnDefinitions = new ColumnDefinitions(main ? "Auto,*" : "*,Auto");
        Grid.SetColumn(_clockStack, main ? 1 : 0);
        _clockStack.HorizontalAlignment = main ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        foreach (var line in _clockStack.Children) line.HorizontalAlignment = main ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        foreach (var weather in new Control[] { _weatherLine, _weatherAdd })
        {
            Grid.SetColumn(weather, main ? 0 : 1);
            weather.HorizontalAlignment = main ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            weather.Margin = main ? new Thickness(0, weather.Margin.Top, 12, 0) : new Thickness(12, weather.Margin.Top, 0, 0);
        }
        foreach (var line in _weatherLine.Children) line.HorizontalAlignment = main ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        // Below the line: the volume on the left, the batteries on the right.
        bool status = _home.IsSwapped(HomeLayout.Status);
        _statusRow.ColumnDefinitions = new ColumnDefinitions(status ? "Auto,*" : "*,Auto");
        Grid.SetColumn(_soundPanel, status ? 0 : 1);
        Grid.SetColumn(_devicesPanel, status ? 1 : 0);
        _devicesPanel.HorizontalAlignment = status ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        if (status != _devicesSwapped)
        {
            _devicesSwapped = status;
            RefreshDevices(); // the chips keep their gap toward the middle
        }
        // The cards swap where they are filled in (ShowToday).
    }
}
