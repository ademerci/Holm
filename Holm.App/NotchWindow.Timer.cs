using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// A timer in the notch, like a phone's live activity: what's left takes the equalizer's place while music plays
/// and shows on its own otherwise; open, it has pause and stop. Right-click › Timer starts one.
///
/// Also here, until it has more in it: the right-click menu. Not ported yet: the alarm sound.
/// </summary>
public sealed partial class NotchWindow
{
    const string TimerGlyph = "";
    static readonly int[] TimerMinutes = { 1, 5, 10, 15, 25, 30, 45, 60 };

    Countdown? _timer;
    readonly DispatcherTimer _timerTick = new() { Interval = TimeSpan.FromSeconds(1) };
    string? _shownTimerText;

    // Timer: a timer runs and nothing plays
    readonly Grid _timerView = new() { Margin = new Thickness(14, 0) };
    readonly TextBlock _timerText = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TimerOrange, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>In the compact player, a running timer takes the equalizer's place.</summary>
    readonly TextBlock _compactTimer = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TimerOrange, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };

    // The open notch: what's left, pause and stop
    readonly Grid _timerRow = new() { Margin = new Thickness(2, 12, 2, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), IsVisible = false };
    readonly TextBlock _timerRowText = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = TimerOrange, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _timerRowLength = new() { FontSize = 12, Foreground = Dim, Margin = new Thickness(10, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _timerPauseIcon = new() { Text = "", FontFamily = IconFont, FontSize = 13 };

    readonly MenuItem _menuTimer = new() { Header = "Timer" };
    bool _menuOpen;

    void BuildTimer()
    {
        _timerTick.Tick += (_, _) => OnTimerTick();

        _timerView.Children.Add(new TextBlock { Text = TimerGlyph, FontFamily = IconFont, FontSize = 14, Foreground = TimerOrange, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        _timerView.Children.Add(_timerText);

        _timerRow.Children.Add(new TextBlock { Text = TimerGlyph, FontFamily = IconFont, FontSize = 14, Foreground = TimerOrange, VerticalAlignment = VerticalAlignment.Center });
        var texts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_timerRowText);
        texts.Children.Add(_timerRowLength);
        Grid.SetColumn(texts, 1);
        _timerRow.Children.Add(texts);
        var pause = new NotchButton(_timerPauseIcon) { Width = 30, Height = 30 };
        pause.Click += ToggleTimerPause;
        var stop = new NotchButton(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 13 }) { Width = 30, Height = 30, Margin = new Thickness(2, 0, 0, 0) };
        stop.Click += StopTimer;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(pause);
        buttons.Children.Add(stop);
        Grid.SetColumn(buttons, 2);
        _timerRow.Children.Add(buttons);
    }

    /// <summary>
    /// The right-click menu of the notch, as in the WPF app: what you use (the timer), Settings… with the switches,
    /// Customize with the settings that need a choice, and Quit.
    /// </summary>
    void BuildContextMenu()
    {
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => OpenSettings();
        var bounceLevel = new MenuItem { Header = "Bounce" };
        var weather = new MenuItem { Header = "Weather" };
        var collapsedSize = new MenuItem { Header = "Collapsed size" };
        var expandedSize = new MenuItem { Header = "Expanded size" };
        var messages = new MenuItem { Header = "Messages" };
        var quiet = new MenuItem { Header = "Quiet hours" };
        var customize = new MenuItem { Header = "Customize" };
        customize.Items.Add(messages);
        customize.Items.Add(quiet);
        customize.Items.Add(weather);
        customize.Items.Add(collapsedSize);
        customize.Items.Add(expandedSize);
        customize.Items.Add(bounceLevel);
        var receive = new MenuItem { Header = "Receive with copied code" };
        receive.Click += (_, _) => _ = ReceiveCopiedCodeAsync();
        var friends = new MenuItem { Header = "Friends" };
        var hub = new MenuItem { Header = "Friends and chat…" };
        hub.Click += (_, _) => Dispatcher.UIThread.Post(() => OpenChat(null), DispatcherPriority.Background);
        var menu = new ContextMenu();
        menu.Items.Add(_menuTimer);
        menu.Items.Add(_menuSleep);
        menu.Items.Add(receive);
        menu.Items.Add(friends);
        menu.Items.Add(new Separator());
        menu.Items.Add(hub);
        menu.Items.Add(settings);
        menu.Items.Add(customize);
        menu.Items.Add(_monitorSeparator);
        // one entry per display is inserted here when the menu opens
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        menu.Opening += (_, _) =>
        {
            _menuOpen = true;
            FillTimerMenu();
            FillSleepMenu();
            FillBounceMenu(bounceLevel);
            ListScreens(menu);
            FillWeatherMenu(weather);
            FillMessagesMenu(messages);
            FillFriendsMenu(friends);
            receive.IsEnabled = _settings.Beam;
            FillQuietMenu(quiet);
            FillSizeMenu(collapsedSize, expanded: false);
            FillSizeMenu(expandedSize, expanded: true);
        };
        // The notch stays as it is while its menu is open; once it has closed, it closes too if the mouse has gone.
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_hovered && !_island.IsPointerOver && !_typing) SetExpanded(false);
        };
        _island.ContextMenu = menu;
    }

    void StartTimer(TimeSpan length)
    {
        _timer = new Countdown(length, DateTime.UtcNow);
        Diag.Log($"Timer started: {Countdown.Describe(length)}");
        ShowTimer();
        ApplyState(animate: true);
    }

    void StopTimer()
    {
        _timer = null;
        ShowTimer();
        ApplyState(animate: true);
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
        bool shown = timer != null;
        if (_compactTimer.IsVisible != shown)
        {
            _compactTimer.IsVisible = shown;
            _eqPanel.IsVisible = !shown;
            UpdateEqualizer();
        }
        if (_timerRow.IsVisible != shown)
        {
            _timerRow.IsVisible = shown;
            if (_state == NotchState.Expanded) ApplyState(animate: true); // the open notch gets taller or shorter
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
            _compactTimer.Text = _timerText.Text = _timerRowText.Text = text;
        }
        double opacity = timer.Paused ? 0.5 : 1;
        _compactTimer.Opacity = _timerText.Opacity = _timerRowText.Opacity = opacity;
        _timerRowLength.Text = timer.Paused ? "Paused" : Countdown.Describe(timer.Length);
        _timerPauseIcon.Text = timer.Paused ? "" : "";

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
        string length = Countdown.Describe(timer.Length);
        ShowTask(TimerGlyph, TimerOrange, "Timer done", length + (length.StartsWith("1 ") ? " is up" : " are up"), null,
                 ("+5 min", () => { HideTask(); StartTimer(TimeSpan.FromMinutes(5)); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(30);
        _taskHide.Start();
    }

    /// <summary>Timer › 1 minute … 1 hour; while one runs: Pause or Resume, more time, Stop.</summary>
    void FillTimerMenu()
    {
        _menuTimer.Items.Clear();
        void Add(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            _menuTimer.Items.Add(item);
        }

        if (_timer is { } timer)
        {
            _menuTimer.Header = $"Timer · {Countdown.Format(timer.Remaining(DateTime.UtcNow))}";
            Add(timer.Paused ? "Resume" : "Pause", ToggleTimerPause);
            Add("+1 minute", () => AddTimerTime(TimeSpan.FromMinutes(1)));
            Add("+5 minutes", () => AddTimerTime(TimeSpan.FromMinutes(5)));
            Add("Stop", StopTimer);
            return;
        }
        _menuTimer.Header = "Timer";
        foreach (int minutes in TimerMinutes)
        {
            var length = TimeSpan.FromMinutes(minutes);
            Add(Countdown.Describe(length), () => StartTimer(length));
        }
    }
}
