using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// Reminders typed in plain words ("20 min laundry", "14:30 dentist") in the open notch; the notch speaks up on
/// time with "+10 min" and "OK". They are kept in the settings, so they survive a restart; one missed while the PC
/// was off shows as soon as Holm starts.
/// </summary>
public partial class MainWindow
{
    const string BellGlyph = "";
    ReminderList _reminders = null!;
    DispatcherTimer _reminderTick = null!;

    void InitReminders()
    {
        _reminders = new ReminderList(_settings.ReminderList);
        _reminderTick = MakeTimer(60_000, CheckReminders, oneShot: true);
        AllowTyping(ReminderBox);
    }

    /// <summary>Shows what has come due and plans the next look for the moment the next one is due.</summary>
    void CheckReminders()
    {
        _reminderTick.Stop();
        if (!_settings.Reminders) return;
        var due = _reminders.TakeDue(DateTime.UtcNow);
        if (due.Count > 0)
        {
            _settings.Save();
            RemindNow(due);
            ShowReminderRows();
        }
        if (_reminders.Wait(DateTime.UtcNow) is { } wait)
        {
            _reminderTick.Interval = wait + TimeSpan.FromMilliseconds(50);
            _reminderTick.Start();
        }
    }

    void RemindNow(List<Reminder> due)
    {
        var first = due[0];
        Diag.Log($"Reminder due ({due.Count})");
        PlayAlarm();
        string when = first.Due.ToLocalTime().ToString("HH:mm");
        string subtitle = due.Count == 1 ? $"Reminder · {when}" : $"Reminder · {when} · and {due.Count - 1} more";
        if (_busy || _installingUpdate)
        {
            ShowPeek(BellGlyph, first.Text, when, null, false, TimerOrange, seconds: 10);
            return;
        }
        ShowTask(BellGlyph, TimerOrange, first.Text, subtitle, null,
                 ("+10 min", () => { HideTask(); AddReminder(first.Text, DateTime.UtcNow.AddMinutes(10)); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(60);
        _taskHide.Start();
    }

    void AddReminder(string text, DateTime dueUtc)
    {
        _reminders.Add(text, dueUtc);
        _settings.Save();
        ShowReminderRows();
        CheckReminders();
    }

    // ───────────── the page ─────────────

    void ReminderBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ReminderPlaceholder.Visibility = ReminderBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var now = DateTime.Now;
        if (ReminderBox.Text.Trim().Length == 0)
            ReminderHint.Text = "Type a time and what for – Enter to add";
        else if (ReminderParser.TryParse(ReminderBox.Text, now, out var due, out var text))
            ReminderHint.Text = $"{text} · {ReminderParser.Describe(due, now)} · Enter to add";
        else
            ReminderHint.Text = "Add a time: 20 min, 1 h, 14:30, 9 Uhr, tomorrow 8:15";
    }

    void ReminderBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var now = DateTime.Now;
        if (!ReminderParser.TryParse(ReminderBox.Text, now, out var due, out var text)) return;
        ReminderBox.Text = "";
        AddReminder(text, due.ToUniversalTime());
    }

    void ShowReminderRows()
    {
        ReminderRows.Children.Clear();
        var now = DateTime.Now;
        foreach (var reminder in _reminders.Items.Take(6))
        {
            var row = new Grid { Margin = new Thickness(2, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = BellGlyph, FontFamily = IconFont, FontSize = 12, Foreground = TimerOrange, VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBlock { Text = reminder.Text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var when = new TextBlock { Text = ReminderParser.Describe(reminder.Due.ToLocalTime(), now), FontSize = 12, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(when, 2);
            row.Children.Add(when);
            var remove = new Button { Style = (Style)FindResource("NotchButton"), Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove" };
            remove.Content = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 9, Foreground = DimBrush };
            string id = reminder.Id;
            remove.Click += (_, _) =>
            {
                _reminders.Remove(id);
                _settings.Save();
                ShowReminderRows();
                CheckReminders();
            };
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            ReminderRows.Children.Add(row);
        }
        if (_reminders.Items.Count > 6)
            ReminderRows.Children.Add(new TextBlock { Text = $"+{_reminders.Items.Count - 6} more", FontSize = 11, Foreground = DimBrush, Margin = new Thickness(24, 0, 0, 0) });
        ShowIdle();
        ShowToday();
        if (_expanded && _state == NotchState.Expanded && _tool == Tool.Reminders) ApplyState();
    }

    void ShowRemindersPage()
    {
        ReminderBox_TextChanged(ReminderBox, null!);
        ShowReminderRows();
    }

    void RemindersTool_Click(object sender, RoutedEventArgs e) => ToggleTool(Tool.Reminders);

    void MenuReminders_Click(object sender, RoutedEventArgs e)
    {
        _settings.Reminders = MenuReminders.IsChecked;
        _settings.Save();
        RefreshToolBar();
        CheckReminders();
    }
}
