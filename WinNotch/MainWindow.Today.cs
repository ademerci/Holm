using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The quiet states. Idle, the notch shows the time (or the weather) and on its right the one thing that matters
/// most: the next reminder today, else what's on the shelf. Open without music, it is a card for today: time and
/// date, the weather, what's next, and the note. Tools stay in the shared top bar.
/// </summary>
public partial class MainWindow
{
    DispatcherTimer _idleClock = null!;

    void InitToday() => _idleClock = MakeTimer(60_000, ShowIdle, oneShot: true);

    /// <summary>The idle notch: called whenever one of its parts may have changed, and on the minute.</summary>
    void ShowIdle()
    {
        var now = DateTime.Now;
        IdleClock.Text = now.ToString("HH:mm");
        IdleClock.Visibility = IdleWeather.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        var next = _settings.Reminders ? _reminders.Items.FirstOrDefault() : null;
        var status = Today.Pick(next?.Due, DateTime.UtcNow, ShelfItems.Count);
        IdleReminder.Visibility = status == IdleStatus.Reminder ? Visibility.Visible : Visibility.Collapsed;
        IdleShelf.Visibility = status == IdleStatus.Shelf ? Visibility.Visible : Visibility.Collapsed;
        if (next != null) IdleReminderTime.Text = next.Due.ToLocalTime().ToString("HH:mm");

        // Again on the next minute.
        _idleClock.Stop();
        _idleClock.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _idleClock.Start();
    }

    /// <summary>The today card of the open notch.</summary>
    void ShowToday()
    {
        var (reminder, when) = Today.ReminderCard(_reminders.Items.FirstOrDefault(), DateTime.Now);
        TodayReminderTitle.Text = reminder;
        TodayReminderWhen.Text = when;
        var (note, more) = Today.NoteCard(_settings.NoteText);
        TodayNoteTitle.Text = note;
        TodayNoteMore.Text = more;

        TodayReminder.Visibility = _settings.Reminders ? Visibility.Visible : Visibility.Collapsed;
        TodayNote.Visibility = _settings.Notes ? Visibility.Visible : Visibility.Collapsed;
        // One card alone takes the whole row.
        System.Windows.Controls.Grid.SetColumnSpan(TodayReminder, _settings.Notes ? 1 : 3);
        System.Windows.Controls.Grid.SetColumn(TodayNote, _settings.Reminders ? 2 : 0);
        System.Windows.Controls.Grid.SetColumnSpan(TodayNote, _settings.Reminders ? 1 : 3);
        TodayCards.Visibility = _settings.Reminders || _settings.Notes ? Visibility.Visible : Visibility.Collapsed;

    }

    /// <summary>Music started or stopped while open: the player and the card cross-fade (unless animations are off).</summary>
    void FadeIn(UIElement panel)
    {
        if (!_settings.Animations || !_expanded) { panel.BeginAnimation(OpacityProperty, null); panel.Opacity = 1; return; }
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuadraticEase() });
    }

    void TodayReminder_Click(object sender, RoutedEventArgs e) => ToggleTool(Tool.Reminders);

    void TodayNote_Click(object sender, RoutedEventArgs e) => ToggleTool(Tool.Notes);
}
