using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// A timer in the notch, like a phone's live activity: what's left takes the equalizer's place while music plays
/// and shows on its own otherwise; open, it has pause and stop. Right-click › Timer starts one.
/// </summary>
public partial class MainWindow
{
    static readonly Brush TimerOrange = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A));
    static readonly int[] TimerMinutes = { 1, 5, 10, 15, 25, 30, 45, 60 };
    static SoundPlayer? _alarm; // kept alive while it plays

    Countdown? _timer;
    DispatcherTimer _timerTick = null!;
    string? _shownTimerText;

    void InitTimer() => _timerTick = MakeTimer(1000, OnTimerTick);

    void StartTimer(TimeSpan length)
    {
        _timer = new Countdown(length, DateTime.UtcNow);
        Diag.Log($"Timer started: {Countdown.Describe(length)}");
        ShowTimer();
        ApplyState();
    }

    void StopTimer()
    {
        _timer = null;
        ShowTimer();
        ApplyState();
    }

    void ToggleTimerPause()
    {
        if (_timer is not { } timer) return;
        if (timer.Paused) timer.Resume(DateTime.UtcNow);
        else timer.Pause(DateTime.UtcNow);
        ShowTimer();
    }

    void AddTimerTime(TimeSpan more)
    {
        if (_timer is not { } timer) return;
        timer.Add(more, DateTime.UtcNow);
        ShowTimer();
    }

    void OnTimerTick()
    {
        if (_timer is not { } timer) { _timerTick.Stop(); return; }
        if (timer.IsDone(DateTime.UtcNow)) TimerDone(timer);
        else ShowTimer();
    }

    /// <summary>
    /// Puts what's left everywhere it shows and plans the next tick for the moment the shown second changes,
    /// so it never skips one. Only runs while a timer counts down.
    /// </summary>
    void ShowTimer()
    {
        var timer = _timer;
        var shown = timer != null ? Visibility.Visible : Visibility.Collapsed;
        if (CompactTimer.Visibility != shown)
        {
            CompactTimer.Visibility = shown;
            EqPanel.Visibility = timer != null ? Visibility.Collapsed : Visibility.Visible;
            UpdateEqualizer();
        }
        if (TimerRow.Visibility != shown)
        {
            TimerRow.Visibility = shown;
            if (_expanded && _state == NotchState.Expanded) ApplyState(); // the open notch gets taller or shorter
        }
        if (timer == null)
        {
            _timerTick.Stop();
            _shownTimerText = null;
            return;
        }

        var left = timer.Remaining(DateTime.UtcNow);
        string text = Countdown.Format(left);
        if (text != _shownTimerText)
        {
            _shownTimerText = text;
            CompactTimer.Text = TimerText.Text = TimerRowText.Text = text;
        }
        double opacity = timer.Paused ? 0.5 : 1;
        CompactTimer.Opacity = TimerText.Opacity = TimerRowText.Opacity = opacity;
        TimerRowLength.Text = timer.Paused ? "Paused" : Countdown.Describe(timer.Length);
        TimerPauseIcon.Text = timer.Paused ? "" : "";
        TimerPauseBtn.ToolTip = timer.Paused ? "Resume" : "Pause";

        if (timer.Paused)
        {
            _timerTick.Stop();
            return;
        }
        _timerTick.Interval = TimeSpan.FromMilliseconds(left.TotalMilliseconds % 1000 + 15);
        if (!_timerTick.IsEnabled) _timerTick.Start();
    }

    void TimerDone(Countdown timer)
    {
        _timer = null;
        ShowTimer();
        Diag.Log("Timer done");
        PlayAlarm();
        string length = Countdown.Describe(timer.Length);
        if (_busy)
        {
            // A transfer is showing: don't take its place.
            ShowPeek("", "Timer done", length, null, false, TimerOrange, seconds: 8);
            return;
        }
        ShowTask("", TimerOrange, "Timer done", length + (length.StartsWith("1 ") ? " is up" : " are up"), null,
                 ("+5 min", () => { HideTask(); StartTimer(TimeSpan.FromMinutes(5)); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(30);
        _taskHide.Start();
    }

    static void PlayAlarm()
    {
        try
        {
            string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (File.Exists(wav))
            {
                _alarm = new SoundPlayer(wav);
                _alarm.Play();
                return;
            }
        }
        catch (Exception ex) { Diag.Log("Alarm sound: " + ex.Message); }
        SystemSounds.Exclamation.Play();
    }

    /// <summary>Timer › 1 minute … 1 hour; while one runs: Pause or Resume, more time, Stop.</summary>
    void FillTimerMenu()
    {
        MenuTimer.Items.Clear();
        void Add(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            MenuTimer.Items.Add(item);
        }

        if (_timer is { } timer)
        {
            MenuTimer.Header = $"Timer · {Countdown.Format(timer.Remaining(DateTime.UtcNow))}";
            Add(timer.Paused ? "Resume" : "Pause", ToggleTimerPause);
            Add("+1 minute", () => AddTimerTime(TimeSpan.FromMinutes(1)));
            Add("+5 minutes", () => AddTimerTime(TimeSpan.FromMinutes(5)));
            Add("Stop", StopTimer);
            return;
        }
        MenuTimer.Header = "Timer";
        foreach (int minutes in TimerMinutes)
        {
            var length = TimeSpan.FromMinutes(minutes);
            Add(Countdown.Describe(length), () => StartTimer(length));
        }
    }

    void TimerPause_Click(object sender, RoutedEventArgs e) => ToggleTimerPause();
    void TimerStop_Click(object sender, RoutedEventArgs e) => StopTimer();
}
