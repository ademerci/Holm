using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The sleep timer: pauses the music after 15, 30 or 60 minutes. Set from the moon in the open player or from
/// right-click › Sleep timer; the moon shows the minutes left while it runs.
/// </summary>
public partial class MainWindow
{
    readonly SleepTimer _sleep = new();
    DispatcherTimer _sleepTick = null!;
    ContextMenu? _sleepMenu;

    void InitSleep() => _sleepTick = MakeTimer(60_000, OnSleepTick, oneShot: true);

    void StartSleep(int minutes)
    {
        _sleep.Start(TimeSpan.FromMinutes(minutes), DateTime.UtcNow);
        Diag.Log($"Sleep timer: {minutes} min");
        ShowSleep();
        ShowPeek("", "Sleep timer", $"{minutes} min", null, false, TimerOrange);
    }

    void CancelSleep()
    {
        _sleep.Cancel();
        ShowSleep();
    }

    /// <summary>Fires when the minute shown changes, and right at the end.</summary>
    void OnSleepTick()
    {
        if (_sleep.TakeDone(DateTime.UtcNow))
        {
            Diag.Log("Sleep timer done");
            ShowSleep();
            if (_info?.IsPlaying == true)
            {
                _media.Pause();
                ShowPeek("", "Sleep timer", "Music paused", null, false, DimBrush);
            }
            return;
        }
        ShowSleep();
    }

    /// <summary>The moon in the player and its minutes; plans the next tick for when the minute changes.</summary>
    void ShowSleep()
    {
        SleepBtn.Visibility = _settings.SleepTimer ? Visibility.Visible : Visibility.Collapsed;
        if (!_sleep.Running)
        {
            _sleepTick.Stop();
            SleepText.Visibility = Visibility.Collapsed;
            SleepIcon.Foreground = (System.Windows.Media.Brush)FindResource("Faint");
            SleepBtn.ToolTip = "Sleep timer";
            return;
        }
        var left = _sleep.Remaining(DateTime.UtcNow);
        SleepText.Text = SleepTimer.Short(left);
        SleepText.Visibility = Visibility.Visible;
        SleepIcon.Foreground = TimerOrange;
        SleepBtn.ToolTip = $"Pauses the music in {SleepTimer.Short(left)}";
        _sleepTick.Stop();
        _sleepTick.Interval = SleepTimer.UntilNextMinute(left) + TimeSpan.FromMilliseconds(15);
        _sleepTick.Start();
    }

    void FillSleepItems(ItemsControl menu)
    {
        menu.Items.Clear();
        foreach (int minutes in SleepTimer.Choices)
        {
            var item = new MenuItem { Header = Countdown.Describe(TimeSpan.FromMinutes(minutes)), IsCheckable = true };
            item.IsChecked = _sleep.Running && _sleep.Length == TimeSpan.FromMinutes(minutes);
            item.Click += (_, _) => StartSleep(minutes);
            menu.Items.Add(item);
        }
        if (_sleep.Running)
        {
            var off = new MenuItem { Header = $"Off ({SleepTimer.Short(_sleep.Remaining(DateTime.UtcNow))} left)" };
            off.Click += (_, _) => CancelSleep();
            menu.Items.Add(off);
        }
    }

    /// <summary>Sleep timer › 15 minutes, 30 minutes, 1 hour, Off; and whether the moon shows in the player.</summary>
    void FillSleepMenu()
    {
        FillSleepItems(MenuSleep);
        MenuSleep.Header = _sleep.Running ? $"Sleep timer · {SleepTimer.Short(_sleep.Remaining(DateTime.UtcNow))}" : "Sleep timer";
        MenuSleep.Items.Add(new Separator());
        var button = new MenuItem { Header = "Moon button in the player", IsCheckable = true, IsChecked = _settings.SleepTimer };
        button.Click += (_, _) =>
        {
            _settings.SleepTimer = button.IsChecked;
            _settings.Save();
            ShowSleep();
        };
        MenuSleep.Items.Add(button);
    }

    void Sleep_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SleepBtn, Placement = PlacementMode.Bottom };
        FillSleepItems(menu);
        menu.Closed += Menu_Closed;
        _sleepMenu = menu;
        menu.IsOpen = true;
    }
}
