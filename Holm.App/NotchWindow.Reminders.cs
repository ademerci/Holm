using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Reminders typed in plain words ("20 min laundry", "14:30 dentist") in the open notch; the notch speaks up on
/// time with "+10 min" and "OK". They are kept in the settings, so they survive a restart; one missed while the PC
/// was off shows as soon as Holm starts.
///
/// Not ported yet: the alarm sound. And as long as the port's settings are read-only, a reminder added here is
/// gone when the port closes.
/// </summary>
public sealed partial class NotchWindow
{
    const string BellGlyph = "";
    ReminderList _reminders = null!;
    readonly DispatcherTimer _reminderTick = new();
    long _lastReminderRemove; // a double-click on × must not also delete the row that slides into place

    readonly StackPanel _remindersPage = new() { IsVisible = false };
    readonly TextBox _reminderBox = NotchTextBox();
    readonly TextBlock _reminderPlaceholder = Placeholder("20 min laundry · 14:30 dentist · tomorrow 9:00 call");
    readonly TextBlock _reminderHint = new() { FontSize = 11, Foreground = Dim, Margin = new Thickness(4, 6, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly StackPanel _reminderRows = new();

    // Idle: the next reminder today, on the right of the closed notch
    readonly StackPanel _idleReminder = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly TextBlock _idleReminderTime = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TimerOrange, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    Control BuildRemindersPage()
    {
        _reminders = new ReminderList(_settings.ReminderList);
        _reminderTick.Tick += (_, _) => CheckReminders();

        _idleReminder.Children.Add(new TextBlock { Text = BellGlyph, FontFamily = IconFont, FontSize = 10, Foreground = TimerOrange, VerticalAlignment = VerticalAlignment.Center });
        _idleReminder.Children.Add(_idleReminderTime);
        _idleView.Children.Add(_idleReminder);

        _reminderBox.MaxLength = 200;
        _reminderBox.TextChanged += (_, _) => OnReminderTyped();
        _reminderBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var now = DateTime.Now;
            if (!ReminderParser.TryParse(_reminderBox.Text, now, out var due, out var text))
            {
                if ((_reminderBox.Text ?? "").Trim().Length > 0) _reminderHint.Text = "Couldn’t find a time – try “20 min laundry” or “14:30 dentist”";
                return;
            }
            _reminderBox.Text = "";
            AddReminder(text, due.ToUniversalTime());
        }, RoutingStrategies.Tunnel);
        AllowTyping(_reminderBox);
        var field = new Grid();
        field.Children.Add(_reminderBox);
        field.Children.Add(_reminderPlaceholder);
        _remindersPage.Children.Add(PageTitle("Reminders"));
        _remindersPage.Children.Add(field);
        _remindersPage.Children.Add(_reminderHint);
        _remindersPage.Children.Add(_reminderRows);
        return _remindersPage;
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
        string when = first.Due.ToLocalTime().ToString("HH:mm");
        string subtitle = due.Count == 1 ? $"Reminder · {when}" : $"Reminder · {when} · and {due.Count - 1} more";
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

    void OnReminderTyped()
    {
        string typed = _reminderBox.Text ?? "";
        _reminderPlaceholder.IsVisible = typed.Length == 0;
        var now = DateTime.Now;
        if (typed.Trim().Length == 0)
            _reminderHint.Text = "Type a time and what for – Enter to add";
        else if (ReminderParser.TryParse(typed, now, out var due, out var text))
            _reminderHint.Text = $"{text} · {ReminderParser.Describe(due, now)} · Enter to add";
        else
            _reminderHint.Text = "Add a time: 20 min, 1 h, 14:30, 9 Uhr, tomorrow 8:15";
    }

    void ShowReminderRows()
    {
        _reminderRows.Children.Clear();
        var now = DateTime.Now;
        foreach (var reminder in _reminders.Items.Take(6))
        {
            var row = new Grid { Margin = new Thickness(2, 0, 0, 6), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
            row.Children.Add(new TextBlock { Text = BellGlyph, FontFamily = IconFont, FontSize = 12, Foreground = TimerOrange, VerticalAlignment = VerticalAlignment.Center });
            var text = new TextBlock { Text = reminder.Text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var when = new TextBlock { Text = ReminderParser.Describe(reminder.Due.ToLocalTime(), now), FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(when, 2);
            row.Children.Add(when);
            var remove = new NotchButton(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 9, Foreground = Dim })
            {
                Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0),
            };
            string id = reminder.Id;
            remove.Click += () =>
            {
                if (Environment.TickCount64 - _lastReminderRemove < 400) return;
                _lastReminderRemove = Environment.TickCount64;
                _reminders.Remove(id);
                _settings.Save();
                ShowReminderRows();
                CheckReminders();
            };
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            _reminderRows.Children.Add(row);
        }
        if (_reminders.Items.Count > 6)
            _reminderRows.Children.Add(new TextBlock { Text = $"+{_reminders.Items.Count - 6} more", FontSize = 11, Foreground = Dim, Margin = new Thickness(24, 0, 0, 0) });
        ShowIdleStatus();
        if (_state == NotchState.Expanded && _tool == Tool.Reminders) ApplyState(animate: true);
    }

    void ShowRemindersPage()
    {
        OnReminderTyped();
        ShowReminderRows();
    }

    /// <summary>The right side of the idle notch: the next reminder, when it is within twelve hours.</summary>
    void ShowIdleStatus()
    {
        var next = _settings.Reminders ? _reminders.Items.FirstOrDefault() : null;
        _idleReminder.IsVisible = Today.Pick(next?.Due, DateTime.UtcNow, 0) == IdleStatus.Reminder;
        if (next != null) _idleReminderTime.Text = next.Due.ToLocalTime().ToString("HH:mm");
    }
}
