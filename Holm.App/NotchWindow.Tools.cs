using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The open notch's tool bar: each tool opens a page in place of home, and clicking it again goes back. Tools you
/// type into may take the keyboard: the notch otherwise never takes focus from your app, so it does that only while
/// you click into a text field, and gives it back as soon as you click elsewhere or the notch closes.
///
/// Notes, reminders, the calculator, the clipboard history, the mixer and the colour picker, in the WPF app's order.
/// </summary>
public sealed partial class NotchWindow
{
    enum Tool { None, Notes, Reminders, Calculator, Clipboard, Mixer, Ai }

    static readonly IBrush ToolLit = Look.InkA(0x29);

    readonly StackPanel _toolBar = new()
    {
        Orientation = Orientation.Horizontal, HorizontalAlignment = Look.Pick(HorizontalAlignment.Right, HorizontalAlignment.Center),
        Margin = Look.Pick(new Thickness(8, 0, 4, 0), new Thickness(36, 0, 36, 0)),
    };
    readonly Grid _toolPage = new() { IsVisible = false };
    NotchButton _notesToolBtn = null!, _remindersToolBtn = null!, _calculatorToolBtn = null!, _chatsToolBtn = null!;
    Tool _tool;
    bool _typing;
    bool _typingUntouched; // the field took the keyboard when its tool opened and nothing was typed yet

    // Notes: a short note that survives a restart
    readonly StackPanel _notesPage = new() { IsVisible = false };
    readonly TextBox _notesBox = NotchTextBox();
    readonly TextBlock _notesPlaceholder = Placeholder("Jot something down – it stays here, even after a restart");
    readonly DispatcherTimer _notesSave = new() { Interval = TimeSpan.FromMilliseconds(800) };
    bool _showingNotes; // filling the box, not typing

    // Calculator: the result shows while you type; click it or press Enter to copy
    readonly StackPanel _calculatorPage = new() { IsVisible = false };
    readonly TextBox _calcBox = NotchTextBox();
    readonly TextBlock _calcPlaceholder = Placeholder("12,5 × 4 + 15%");
    readonly TextBlock _calcResultText = new() { FontSize = 26, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0, 4, 0), MinHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _calcHint = new() { FontSize = 11, Foreground = Dim, Margin = new Thickness(4, 2, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
    string? _calcResult;

    /// <summary>The WPF app's NotchTextBox style: a soft rounded field whose border turns blue while you type in it.</summary>
    static TextBox NotchTextBox()
    {
        IBrush fill = Look.Pick<IBrush>(Brushes.Transparent, Look.InkA(0x14)), border = Brush(Look.Pick("#33FFFFFF", "#1FFFFFFF")),
            focused = Brush(Look.Pick("#CCF2F2F2", "#990A84FF"));
        var box = new TextBox
        {
            Foreground = Look.Ink, CaretBrush = Look.Ink, SelectionBrush = Look.Pick(Brush("#FF5A5C61"), Look.Accent), FontSize = 13,
            Background = fill, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Look.Pick(3, 12)),
            Padding = new Thickness(10, 7), MinHeight = 0,
        };
        // The theme repaints a text box under the mouse and with the keyboard in it; keep ours as it is.
        foreach (string state in new[] { "PointerOver", "Focused" })
        {
            box.Resources["TextControlBackground" + state] = fill;
            box.Resources["TextControlForeground" + state] = Look.Ink;
            box.Resources["TextControlBorderBrush" + state] = state == "Focused" ? focused : border;
        }
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        return box;
    }

    static TextBlock Placeholder(string text) => new()
    {
        Text = text, FontSize = 13, Foreground = Faint, Margin = new Thickness(12, 8, 12, 0), IsHitTestVisible = false,
        TextWrapping = TextWrapping.Wrap,
    };

    static TextBlock PageTitle(string text) => new() { Text = text, FontSize = 11, Foreground = Dim, Margin = new Thickness(2, 0, 0, 6) };

    /// <summary>A tool's button: its icon, or in the instrument look a small word.</summary>
    static NotchButton ToolWord(string word, string glyph) => Look.Instrument
        ? new(new TextBlock { Text = Look.Heading(word), FontSize = Look.Current.Caps ? 10 : 11, Foreground = Dim, Margin = new Thickness(4, 0) }) { Height = 28, Margin = new Thickness(1, 0) }
        : ToolWord(word, new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = Look.Aero ? Look.Ink : Dim });

    /// <summary>A tool's button with something other than an icon in the classic look (the AI mark).</summary>
    static NotchButton ToolWord(string word, Control classic)
    {
        if (Look.Instrument) return ToolWord(word, "");
        var button = new NotchButton(classic) { Width = 32, Height = 32, Margin = new Thickness(2, 0) };
        if (Look.Aero)
        {
            button.Rest = HubTheme.ActionSurface;
            button.BorderBrush = HubTheme.Edge;
            button.BorderThickness = new Thickness(1);
        }
        return button;
    }

    NotchButton ToolButton(string word, string glyph, Tool tool, string tip)
    {
        var button = ToolWord(word, glyph);
        ToolTip.SetTip(button, tip);
        button.Click += () => ToggleTool(tool);
        _toolBar.Children.Add(button);
        return button;
    }

    /// <summary>The tool row at the top of the open notch.</summary>
    Control BuildToolRow()
    {
        _notesToolBtn = ToolButton("Notes", "", Tool.Notes, "Notes");
        _remindersToolBtn = ToolButton("Reminders", BellGlyph, Tool.Reminders, "Reminders");
        _calculatorToolBtn = ToolButton("Calc", "", Tool.Calculator, "Calculator");
        _chatsToolBtn = ToolWord("Chats", "\uE8BD");
        ToolTip.SetTip(_chatsToolBtn, "Chats");
        _chatsToolBtn.Click += () =>
        {
            CloseFromInside();
            Dispatcher.UIThread.Post(() => OpenChat(null), DispatcherPriority.Background);
        };
        _toolBar.Children.Add(_chatsToolBtn);
        _clipboardToolBtn = ToolButton("Clipboard", ClipboardGlyph, Tool.Clipboard, "Clipboard history");
        _mixerToolBtn = ToolButton("Mixer", MixerGlyph, Tool.Mixer, "Volume per app");
        _aiToolBtn = ToolWord("AI", AiMarks.For(AiTool.ClaudeCode, 14, Dim));
        ToolTip.SetTip(_aiToolBtn, "AI usage");
        _aiToolBtn.Click += () => ToggleTool(Tool.Ai);
        _toolBar.Children.Add(_aiToolBtn);
        _aiToolBtn.IsVisible = false;
        BuildColorPickerButton();
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12), MinHeight = 32, VerticalAlignment = VerticalAlignment.Top,
            RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        row.Children.Add(_mediaClockHeader);
        Control toolBar = _toolBar;
        if (Look.Prism)
        {
            _toolBar.Margin = new Thickness(2, 0);
            var tray = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = HubTheme.ActionSurface, CornerRadius = new CornerRadius(17),
                Child = new Grid { Children = { _toolBar, new PrismGlass(17) { IsHitTestVisible = false } } },
            };
            tray[!IsVisibleProperty] = _toolBar[!IsVisibleProperty];
            toolBar = tray;
        }
        Grid.SetColumn(toolBar, 1);
        if (!Look.Instrument) { Grid.SetColumn(toolBar, 0); Grid.SetColumnSpan(toolBar, 3); } // the icons in the middle of the row
        row.Children.Add(toolBar);
        var right = BuildToolRowRight();
        Grid.SetColumn(right, 2);
        row.Children.Add(right);
        return row;
    }

    /// <summary>
    /// The tool words sit top right, opposite the clock. With music, the header holds the small clock on the left and
    /// the weather and sleep timer on the right, and eight words don't fit between them: the words take a row of their
    /// own under the clock, left-aligned.
    /// </summary>
    void PlaceToolBar(bool ownRow)
    {
        Grid.SetRow(_toolBar, ownRow ? 1 : 0);
        Grid.SetColumn(_toolBar, ownRow ? 0 : 1);
        Grid.SetColumnSpan(_toolBar, ownRow ? 3 : 1);
        _toolBar.HorizontalAlignment = ownRow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _toolBar.Margin = ownRow ? new Thickness(-4, 10, 0, 0) : new Thickness(8, 0, 4, 0);
    }

    /// <summary>The pages that take home's place.</summary>
    Control BuildToolPages()
    {
        _notesBox.AcceptsReturn = true;
        _notesBox.TextWrapping = TextWrapping.Wrap;
        _notesBox.MinHeight = 96;
        _notesBox.MaxHeight = 170;
        _notesBox.MaxLength = Notepad.MaxLength;
        _notesBox.TextChanged += (_, _) => OnNotesChanged();
        _notesSave.Tick += (_, _) => SaveNotes();
        AllowTyping(_notesBox);
        var notesField = new Grid();
        notesField.Children.Add(_notesBox);
        notesField.Children.Add(_notesPlaceholder);
        _notesPage.Children.Add(PageTitle("Notes"));
        _notesPage.Children.Add(notesField);

        _calcBox.MaxLength = 300;
        _calcBox.TextChanged += (_, _) => OnCalcChanged();
        _calcBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            CopyCalcResult();
        }, RoutingStrategies.Tunnel);
        AllowTyping(_calcBox);
        var calcField = new Grid();
        calcField.Children.Add(_calcBox);
        calcField.Children.Add(_calcPlaceholder);
        var result = NotchButton.Row(_calcResultText);
        result.Margin = new Thickness(0, 8, 0, 0);
        result.Click += CopyCalcResult;
        _calculatorPage.Children.Add(PageTitle("Calculator"));
        _calculatorPage.Children.Add(calcField);
        _calculatorPage.Children.Add(result);
        _calculatorPage.Children.Add(_calcHint);

        _toolPage.Children.Add(_notesPage);
        _toolPage.Children.Add(BuildRemindersPage());
        _toolPage.Children.Add(_calculatorPage);
        _toolPage.Children.Add(BuildClipboardPage());
        _toolPage.Children.Add(BuildMixerPage());
        _toolPage.Children.Add(_aiPage);
        Deactivated += (_, _) => OnDeactivated();
        RefreshToolBar();
        return _toolPage;
    }

    /// <summary>Every tool with its page and its button; a feature adds its own here.</summary>
    (Control Page, NotchButton Button, Tool Tool)[] ToolPages() => new (Control, NotchButton, Tool)[]
    {
        (_notesPage, _notesToolBtn, Tool.Notes),
        (_remindersPage, _remindersToolBtn, Tool.Reminders),
        (_calculatorPage, _calculatorToolBtn, Tool.Calculator),
        (_clipboardPage, _clipboardToolBtn, Tool.Clipboard),
        (_mixerPage, _mixerToolBtn, Tool.Mixer),
        (_aiPage, _aiToolBtn, Tool.Ai),
    };

    /// <summary>Opens a tool's page, or goes back home when it is open already.</summary>
    void ToggleTool(Tool tool)
    {
        ShowTool(_tool == tool ? Tool.None : tool);
        // You clicked the tool to use it: its field takes the keyboard at once, without a second click into it.
        if (_tool == tool && TypingBox(tool) is { } box) BeginTyping(box, untouched: true);
    }

    TextBox? TypingBox(Tool tool) => tool switch
    {
        Tool.Notes => _notesBox,
        Tool.Reminders => _reminderBox,
        Tool.Calculator => _calcBox,
        _ => null,
    };

    /// <summary>Typing keeps the notch open when the mouse leaves; a field that only got the keyboard by opening doesn't.</summary>
    bool KeepOpenForTyping => _typing && !_typingUntouched;

    void ShowTool(Tool tool)
    {
        bool changed = tool != _tool;
        if (tool == Tool.None) EndTyping();
        _tool = tool;
        Control? activePage = null;
        ShowHomeSections();
        _toolPage.IsVisible = tool != Tool.None;
        foreach (var (page, button, which) in ToolPages())
        {
            bool match = which == tool;
            page.IsVisible = match;
            button.Rest = Look.Aero
                ? (match ? HubTheme.Selected : HubTheme.ActionSurface)
                : (match ? ToolLit : Brushes.Transparent);
            if (match) activePage = page;
        }
        if (tool != Tool.Mixer) HideMixer();
        if (tool == Tool.Notes) ShowNotes();
        else if (tool == Tool.Reminders) ShowRemindersPage();
        else if (tool == Tool.Calculator) OnCalcChanged();
        else if (tool == Tool.Clipboard) ShowClipboardPage();
        else if (tool == Tool.Mixer) ShowMixerPage();
        else if (tool == Tool.Ai) FillAiPage();
        if (changed && _hovered && _state == NotchState.Expanded)
        {
            if (activePage != null) AnimateToolSwitch(activePage);
            else foreach (var section in HomeContent()) if (section.IsVisible) AnimateToolSwitch(section);
        }
        if (_state == NotchState.Expanded) ApplyState(animate: true); // a page has its own height
    }

    /// <summary>Which tools are switched on; the bar hides when none is.</summary>
    void RefreshToolBar()
    {
        _notesToolBtn.IsVisible = _settings.Notes;
        _remindersToolBtn.IsVisible = _settings.Reminders;
        _calculatorToolBtn.IsVisible = _settings.Calculator;
        _clipboardToolBtn.IsVisible = _settings.ClipboardHistory;
        _mixerToolBtn.IsVisible = _settings.AppVolume;
        _aiToolBtn.IsVisible = _settings.AiUsage && _aiUsage.Count > 0;
        _colorPickerBtn.IsVisible = _settings.ColorPicker;
        bool any = false;
        foreach (var (_, button, which) in ToolPages())
        {
            if (button.IsVisible) any = true;
            else if (which == _tool) ShowTool(Tool.None); // its switch was turned off while it was open
        }
        _toolBar.IsVisible = any || _colorPickerBtn.IsVisible;
    }

    // ───────────── notes ─────────────

    void ShowNotes()
    {
        _showingNotes = true;
        if (_notesBox.Text != _settings.NoteText) _notesBox.Text = _settings.NoteText;
        _showingNotes = false;
        _notesPlaceholder.IsVisible = string.IsNullOrEmpty(_notesBox.Text);
    }

    void OnNotesChanged()
    {
        _notesPlaceholder.IsVisible = string.IsNullOrEmpty(_notesBox.Text);
        if (_showingNotes) return;
        // Saved a moment after you stop typing, and when you leave the box.
        _notesSave.Stop();
        _notesSave.Start();
        if (_state == NotchState.Expanded) ApplyState(animate: true); // a longer note makes the notch taller
    }

    void SaveNotes()
    {
        _notesSave.Stop();
        string text = Notepad.Clamp(_notesBox.Text ?? "");
        if (text == _settings.NoteText) return;
        _settings.NoteText = text;
        _settings.Save();
        if (text.Trim().Length > 0 && _achievements?.Value(AchievementBook.Notes) == 0) Achieve(AchievementBook.Notes);
    }

    // ───────────── calculator ─────────────

    void OnCalcChanged()
    {
        string typed = _calcBox.Text ?? "";
        _calcPlaceholder.IsVisible = typed.Length == 0;
        _calcResult = Calculator.Evaluate(typed) is { } value ? Calculator.Format(value) : null;
        _calcResultText.Text = _calcResult == null ? (typed.Trim().Length == 0 ? "" : "…") : "= " + _calcResult;
        _calcHint.Text = _calcResult == null ? "+ − × ÷ ^ % ( ) · sqrt, sin, ln, pi" : "Click or Enter to copy";
    }

    async void CopyCalcResult()
    {
        if (_calcResult is not { } result) return;
        try
        {
            if (Clipboard == null) throw new InvalidOperationException("No clipboard.");
            await Clipboard.SetTextAsync(result);
            _calcHint.Text = $"{result} copied";
        }
        catch { _calcHint.Text = "Clipboard busy, try again"; }
    }

    // ───────────── typing ─────────────

    /// <summary>Lets a text field take the keyboard when clicked; Escape gives it back and closes the notch.</summary>
    void AllowTyping(TextBox box)
    {
        box.AddHandler(PointerPressedEvent, (_, _) => BeginTyping(box), RoutingStrategies.Tunnel);
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) { _typingUntouched = false; return; }
            e.Handled = true;
            EndTyping();
            SetExpanded(false);
        }, RoutingStrategies.Tunnel);
    }

    void BeginTyping(TextBox box, bool untouched = false)
    {
        _typingUntouched = untouched;
        if (!_typing && TryGetPlatformHandle() is { } handle)
        {
            _typing = true;
            // The click went to us, so the system lets us take the foreground for the keyboard.
            _platform.SetKeyboard(handle.Handle, take: true);
            Activate();
        }
        box.Focus();
    }

    /// <summary>Back to never taking focus. Called when the notch closes, home shows again or you click elsewhere.</summary>
    void EndTyping()
    {
        if (!_typing) return;
        _typing = false;
        _typingUntouched = false;
        SaveNotes();
        FocusManager?.Focus(null); // nothing holds the keyboard
        if (TryGetPlatformHandle() is { } handle) _platform.SetKeyboard(handle.Handle, take: false);
    }

    /// <summary>You clicked into another app while typing: hand the keyboard back, and close if the mouse is gone.</summary>
    void OnDeactivated()
    {
        if (!_typing) return;
        EndTyping();
        if (!_island.IsPointerOver) SetExpanded(false);
    }
}
