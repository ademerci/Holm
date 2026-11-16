using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The sleep timer (the WPF app's MainWindow.Sleep.cs, with SleepTimer.cs linked): pauses the music after 15, 30
/// or 60 minutes. Set from the moon at the top right of the open notch or from right-click › Sleep timer; the moon
/// shows the minutes left while it runs. The weather sits beside it while music plays, as in the WPF app.
/// </summary>
public sealed partial class NotchWindow
{
    const string MoonGlyph = "";

    readonly SleepTimer _sleep = new();
    readonly DispatcherTimer _sleepTick = new();
    readonly MenuItem _menuSleep = new() { Header = "Sleep timer" };
    readonly TextBlock _sleepIcon = new() { Text = MoonGlyph, FontFamily = IconFont, FontSize = 13, Foreground = Faint };
    readonly TextBlock _sleepText = new() { FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = TimerOrange, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
    readonly StackPanel _mediaWeather = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Background = Brushes.Transparent, MinHeight = 30, IsVisible = false, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
    readonly Border _mediaWeatherIcon = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _mediaWeatherTemp = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    NotchButton _sleepBtn = null!;

    /// <summary>The right end of the tool row: the weather while music plays, and the moon.</summary>
    Control BuildToolRowRight()
    {
        _sleepTick.Tick += (_, _) => { _sleepTick.Stop(); OnSleepTick(); };
        _mediaWeather.Children.Add(_mediaWeatherIcon);
        _mediaWeather.Children.Add(_mediaWeatherTemp);
        _mediaWeather.PointerReleased += (_, _) => { ChoosePlace(); BeginTyping(_weatherBox, untouched: true); };
        _sleepBtn = new NotchButton(_sleepIcon) { Width = 30, Height = 30 };
        ToolTip.SetTip(_sleepBtn, "Sleep timer");
        var menu = new ContextMenu();
        menu.Opening += (_, _) => { _menuOpen = true; FillSleepItems(menu); }; // a right click
        // As with the notch's own menu: the notch stays open while it shows, and closes after if the mouse has gone.
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_hovered && !_island.IsPointerOver && !_typing) { _hovered = false; ApplyState(animate: true); }
        };
        _sleepBtn.ContextMenu = menu;
        _sleepBtn.Click += () =>
        {
            // Opened from code, the menu raises no Opening: fill it first.
            _menuOpen = true;
            FillSleepItems(menu);
            menu.Placement = PlacementMode.Bottom;
            menu.Open(_sleepBtn);
        };
        var moon = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        moon.Children.Add(_sleepBtn);
        moon.Children.Add(_sleepText);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(_mediaWeather);
        right.Children.Add(moon);
        return right;
    }

    void StartSleep(int minutes)
    {
        if (!_settings.MediaControls) return;
        _sleep.Start(TimeSpan.FromMinutes(minutes), DateTime.UtcNow);
        Diag.Log($"Sleep timer: {minutes} min");
        ShowSleep();
        ShowPeek(MoonGlyph, "Sleep timer", $"{minutes} min", null, glyphBrush: TimerOrange);
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
                ShowPeek(MoonGlyph, "Sleep timer", "Music paused", Dim);
            }
            return;
        }
        ShowSleep();
    }

    /// <summary>The moon and its minutes; plans the next tick for when the minute changes.</summary>
    void ShowSleep()
    {
        _sleepBtn.IsVisible = _settings.MediaControls && _settings.SleepTimer;
        _menuSleep.IsVisible = _settings.MediaControls;
        if (!_sleep.Running)
        {
            _sleepTick.Stop();
            _sleepText.IsVisible = false;
            _sleepIcon.Foreground = Faint;
            ToolTip.SetTip(_sleepBtn, "Sleep timer");
            return;
        }
        var left = _sleep.Remaining(DateTime.UtcNow);
        _sleepText.Text = SleepTimer.Short(left);
        _sleepText.IsVisible = true;
        _sleepIcon.Foreground = TimerOrange;
        ToolTip.SetTip(_sleepBtn, $"Pauses the music in {SleepTimer.Short(left)}");
        _sleepTick.Stop();
        _sleepTick.Interval = SleepTimer.UntilNextMinute(left) + TimeSpan.FromMilliseconds(15);
        _sleepTick.Start();
    }

    void FillSleepItems(ItemsControl menu)
    {
        menu.Items.Clear();
        foreach (int minutes in SleepTimer.Choices)
        {
            var item = new MenuItem
            {
                Header = Countdown.Describe(TimeSpan.FromMinutes(minutes)), ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _sleep.Running && _sleep.Length == TimeSpan.FromMinutes(minutes),
            };
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

    /// <summary>Sleep timer › 15 minutes, 30 minutes, 1 hour, Off; and whether the moon shows.</summary>
    void FillSleepMenu()
    {
        FillSleepItems(_menuSleep);
        _menuSleep.Header = _sleep.Running ? $"Sleep timer · {SleepTimer.Short(_sleep.Remaining(DateTime.UtcNow))}" : "Sleep timer";
        _menuSleep.Items.Add(new Separator());
        var button = new MenuItem { Header = "Moon button in the player", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.SleepTimer };
        button.Click += (_, _) =>
        {
            _settings.SleepTimer = !_settings.SleepTimer;
            _settings.Save();
            ShowSleep();
        };
        _menuSleep.Items.Add(button);
    }

    /// <summary>The temperature beside the moon while music plays (the clock's weather is hidden then).</summary>
    void RefreshMediaWeather()
    {
        var weather = _settings.Weather ? _weather : null;
        _mediaWeather.IsVisible = weather != null && _settings.MediaControls && _info != null;
        if (weather == null) return;
        _mediaWeatherIcon.Child = WeatherIcons.Make(weather.Kind, weather.IsDay, 16, Brushes.White);
        _mediaWeatherTemp.Text = weather.Degrees;
        ToolTip.SetTip(_mediaWeather, $"{weather.Summary} · {_settings.WeatherPlace}" + (weather.RainNote != null ? $"\n{weather.RainNote}" : ""));
    }
}
