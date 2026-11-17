using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinNotch;

/// <summary>
/// The open notch's tool bar: each tool (notes, reminders, the calculator, the clipboard, the mixer) opens a page in
/// place of home, and clicking it again goes back. Tools you type into may take the keyboard: the notch otherwise
/// never takes focus from your app, so it does that only while you click into a text field, and gives it back as
/// soon as you click elsewhere or the notch closes.
/// </summary>
public partial class MainWindow
{
    enum Tool { None, Notes, Reminders, Calculator, Clipboard, Mixer, Ai }

    static readonly Brush ToolLit = Frozen(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF));

    Tool _tool;
    bool _typing;
    bool _typingUntouched; // the field took the keyboard when its tool opened and nothing was typed yet

    /// <summary>Opens a tool's page, or goes back home when it is open already.</summary>
    void ToggleTool(Tool tool)
    {
        ShowTool(_tool == tool ? Tool.None : tool);
        // You clicked the tool to use it: its field takes the keyboard at once, without a second click into it.
        if (_tool == tool && TypingBox(tool) is { } box) BeginTyping(box, untouched: true);
    }

    TextBox? TypingBox(Tool tool) => tool switch
    {
        Tool.Notes => NotesBox,
        Tool.Reminders => ReminderBox,
        Tool.Calculator => CalcBox,
        _ => null,
    };

    /// <summary>Typing keeps the notch open when the mouse leaves; a field that only got the keyboard by opening doesn't.</summary>
    bool KeepOpenForTyping => _typing && !_typingUntouched;

    void ShowTool(Tool tool)
    {
        if (_clockViewOpen) SetClockView(false, animate: false);
        bool changed = tool != _tool;
        if (tool == Tool.None) EndTyping();
        _tool = tool;
        HomePanel.Visibility = tool == Tool.None ? Visibility.Visible : Visibility.Collapsed;
        ToolPage.Visibility = tool == Tool.None ? Visibility.Collapsed : Visibility.Visible;
        UIElement? activePage = null;
        foreach (var (page, button, which) in ToolPages())
        {
            bool match = which == tool;
            page.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            button.Background = match ? ToolLit : Brushes.Transparent;
            if (match) activePage = page;
        }
        OnToolShown(tool);
        RefreshToolBar();
        if (changed && _expanded && _state == NotchState.Expanded)
            AnimateToolSwitch(activePage ?? HomePanel);
        if (_expanded && _state == NotchState.Expanded) ApplyState(); // a page has its own height
    }

    /// <summary>Every tool with its page and its button; a feature adds its own here.</summary>
    (FrameworkElement Page, Button Button, Tool Tool)[] ToolPages() => new (FrameworkElement, Button, Tool)[]
    {
        (NotesPage, NotesToolBtn, Tool.Notes),
        (RemindersPage, RemindersToolBtn, Tool.Reminders),
        (CalculatorPage, CalculatorToolBtn, Tool.Calculator),
        (ClipboardPage, ClipboardToolBtn, Tool.Clipboard),
        (MixerPage, MixerToolBtn, Tool.Mixer),
        (AiPage, AiToolBtn, Tool.Ai),
    };

    /// <summary>
    /// The tool words sit top right, opposite the clock. With music, the header holds the small clock on the left and
    /// the weather and sleep timer on the right, and eight words don't fit between them: the words take a row of their
    /// own under the clock, left-aligned.
    /// </summary>
    void PlaceToolBar(bool ownRow)
    {
        Grid.SetRow(ToolBar, ownRow ? 1 : 0);
        Grid.SetColumn(ToolBar, ownRow ? 0 : 1);
        Grid.SetColumnSpan(ToolBar, ownRow ? 3 : 1);
        ToolBar.HorizontalAlignment = ownRow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ToolBar.Margin = ownRow ? new Thickness(-4, 10, 0, 0) : new Thickness(8, 0, 4, 0);
    }

    /// <summary>Called when a page opens, so a tool can fill it in.</summary>
    void OnToolShown(Tool tool)
    {
        if (tool != Tool.Mixer) HideMixer();
        if (tool == Tool.Notes) ShowNotes();
        else if (tool == Tool.Reminders) ShowRemindersPage();
        else if (tool == Tool.Calculator) ShowCalculator();
        else if (tool == Tool.Clipboard) ShowClipboardPage();
        else if (tool == Tool.Mixer) ShowMixerPage();
        else if (tool == Tool.Ai) FillAiPage();
    }

    /// <summary>Which tools are switched on; the bar hides when none is.</summary>
    void RefreshToolBar()
    {
        NotesToolBtn.Visibility = _settings.Notes ? Visibility.Visible : Visibility.Collapsed;
        RemindersToolBtn.Visibility = _settings.Reminders ? Visibility.Visible : Visibility.Collapsed;
        CalculatorToolBtn.Visibility = _settings.Calculator ? Visibility.Visible : Visibility.Collapsed;
        ClipboardToolBtn.Visibility = _settings.ClipboardHistory ? Visibility.Visible : Visibility.Collapsed;
        MixerToolBtn.Visibility = _settings.AppVolume ? Visibility.Visible : Visibility.Collapsed;
        AiToolBtn.Visibility = _settings.AiUsage && _aiUsage.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AiToolBtn.Content ??= new TextBlock { Text = "AI", FontSize = 11, Foreground = HubTheme.Dim, Margin = new Thickness(4, 0, 4, 0) };
        ColorPickerBtn.Visibility = _settings.ColorPicker ? Visibility.Visible : Visibility.Collapsed;
        bool any = ColorPickerBtn.Visibility == Visibility.Visible;
        foreach (var (_, button, which) in ToolPages())
        {
            if (button.Visibility == Visibility.Visible) any = true;
            else if (which == _tool) ShowTool(Tool.None); // its switch was turned off while it was open
        }
        ToolBar.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────── typing ─────────────

    /// <summary>Lets a text field take the keyboard when clicked; Escape gives it back and closes the notch.</summary>
    void AllowTyping(TextBox box)
    {
        box.PreviewMouseLeftButtonDown += (_, _) => BeginTyping(box);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) { _typingUntouched = false; return; }
            e.Handled = true;
            EndTyping();
            SetExpanded(false);
        };
    }

    void BeginTyping(TextBox box, bool untouched = false)
    {
        _typingUntouched = untouched;
        if (!_typing && _hwnd != IntPtr.Zero)
        {
            _typing = true;
            // The click went to us, so Windows lets us take the foreground for the keyboard.
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64() & ~Native.WS_EX_NOACTIVATE;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            Activate();
            Native.SetForegroundWindow(_hwnd);
        }
        box.Focus();
        Keyboard.Focus(box);
    }

    /// <summary>Back to never taking focus. Called when the notch closes, home shows again or you click elsewhere.</summary>
    void EndTyping()
    {
        _typingUntouched = false;
        if (!_typing) return;
        _typing = false;
        SaveNotes();
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, null);
        if (_hwnd == IntPtr.Zero) return;
        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64() | Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>You clicked into another app while typing: hand the keyboard back, and close if the mouse is gone.</summary>
    void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_typing) return;
        EndTyping();
        if (_expanded && !Island.IsMouseOver) _collapseDelay.Start();
    }
}
