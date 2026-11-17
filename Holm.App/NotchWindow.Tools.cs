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
    enum Tool { None, Notes, Reminders, Calculator, Clipboard, Mixer }

    static readonly IBrush ToolLit = Brush("#29FFFFFF");

    readonly StackPanel _toolBar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(36, 0, 36, 0) };
    readonly StackPanel _homePanel = new();
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
        IBrush fill = Brush("#14FFFFFF"), border = Brush("#1FFFFFFF"), focused = Brush("#990A84FF");
        var box = new TextBox
        {
            Foreground = Brushes.White, CaretBrush = Brushes.White, SelectionBrush = Brush("#FF0A84FF"), FontSize = 13,
            Background = fill, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 7), MinHeight = 0,
        };
        // The theme repaints a text box under the mouse and with the keyboard in it; keep ours as it is.
        foreach (string state in new[] { "PointerOver", "Focused" })
        {
            box.Resources["TextControlBackground" + state] = fill;
            box.Resources["TextControlForeground" + state] = Brushes.White;
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

    NotchButton ToolButton(string glyph, Tool tool, string tip)
    {
        var button = new NotchButton(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = Dim })
        {
            Width = 32, Height = 32, Margin = new Thickness(2, 0),
        };
        ToolTip.SetTip(button, tip);
        button.Click += () => ToggleTool(tool);
        _toolBar.Children.Add(button);
        return button;
    }

    /// <summary>The tool row at the top of the open notch.</summary>
    Control BuildToolRow()
    {
        _notesToolBtn = ToolButton("", Tool.Notes, "Notes");
        _remindersToolBtn = ToolButton(BellGlyph, Tool.Reminders, "Reminders");
        _calculatorToolBtn = ToolButton("", Tool.Calculator, "Calculator");
        _chatsToolBtn = new NotchButton(new TextBlock { Text = "\uE8BD", FontFamily = IconFont, FontSize = 14, Foreground = Dim })
        {
            Width = 32, Height = 32, Margin = new Thickness(2, 0),
        };
        ToolTip.SetTip(_chatsToolBtn, "Chats");
        _chatsToolBtn.Click += () =>
        {
            CloseFromInside();
            Dispatcher.UIThread.Post(() => OpenChat(null), DispatcherPriority.Background);
        };
        _toolBar.Children.Add(_chatsToolBtn);
        _clipboardToolBtn = ToolButton(ClipboardGlyph, Tool.Clipboard, "Clipboard history");
        _mixerToolBtn = ToolButton(MixerGlyph, Tool.Mixer, "Volume per app");
        BuildColorPickerButton();
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12), MinHeight = 32, VerticalAlignment = VerticalAlignment.Top };
        row.Children.Add(_mediaClockHeader);
        row.Children.Add(_toolBar);
        row.Children.Add(BuildToolRowRight());
        return row;
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
        _homePanel.IsVisible = tool == Tool.None;
        _toolPage.IsVisible = tool != Tool.None;
        foreach (var (page, button, which) in ToolPages())
        {
            bool match = which == tool;
            page.IsVisible = match;
            button.Rest = match ? ToolLit : Brushes.Transparent;
            if (match) activePage = page;
        }
        if (tool != Tool.Mixer) HideMixer();
        if (tool == Tool.Notes) ShowNotes();
        else if (tool == Tool.Reminders) ShowRemindersPage();
        else if (tool == Tool.Calculator) OnCalcChanged();
        else if (tool == Tool.Clipboard) ShowClipboardPage();
        else if (tool == Tool.Mixer) ShowMixerPage();
        if (changed && _hovered && _state == NotchState.Expanded) AnimateToolSwitch(activePage ?? _homePanel);
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
