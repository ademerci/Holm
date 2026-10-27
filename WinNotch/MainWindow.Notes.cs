using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// A small note in the open notch, kept in the settings so it survives a restart. It stays on this PC: it is never
/// logged or sent anywhere.
/// </summary>
public partial class MainWindow
{
    const int NotesMax = Notepad.MaxLength;
    DispatcherTimer _notesSave = null!;
    bool _showingNotes; // filling the box, not typing

    void InitNotes()
    {
        _notesSave = MakeTimer(800, SaveNotes, oneShot: true);
        AllowTyping(NotesBox);
        NotesBox.MaxLength = NotesMax;
    }

    void ShowNotes()
    {
        _showingNotes = true;
        if (NotesBox.Text != _settings.NoteText) NotesBox.Text = _settings.NoteText;
        _showingNotes = false;
        NotesPlaceholder.Visibility = NotesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NotesPlaceholder.Visibility = NotesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_showingNotes) return;
        // Saved a moment after you stop typing, and when you leave the box.
        _notesSave.Stop();
        _notesSave.Start();
        if (_expanded && _state == NotchState.Expanded) ApplyState(); // a longer note makes the notch taller
    }

    void SaveNotes()
    {
        _notesSave.Stop();
        string text = Notepad.Clamp(NotesBox.Text);
        if (text == _settings.NoteText) return;
        _settings.NoteText = text;
        _settings.Save();
        ShowToday();
    }

    void NotesTool_Click(object sender, RoutedEventArgs e) => ToggleTool(Tool.Notes);

    void MenuNotes_Click(object sender, RoutedEventArgs e)
    {
        _settings.Notes = MenuNotes.IsChecked;
        _settings.Save();
        RefreshToolBar();
    }
}
