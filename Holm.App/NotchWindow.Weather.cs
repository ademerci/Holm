using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The weather where you say you are (the WPF app's MainWindow.Weather.cs, with its Weather.cs linked): the
/// temperature and a symbol in the idle notch, the sky and the next hour's rain beside the clock in the open notch.
/// From Open-Meteo, at most every 30 minutes, for a city you type. Finding the place by itself (the WPF app asks
/// Windows' location once) is not ported: no system offers that the same way, so every system asks for the city.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly IBrush RainBlue = Brush("#FF64D2FF");

    WeatherNow? _weather;
    DateTime? _weatherAt;
    readonly DispatcherTimer _weatherTick = new() { Interval = Weather.Every };
    bool _fetchingWeather, _choosingPlace;
    CancellationTokenSource? _placeSearch;

    readonly StackPanel _idleWeather = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly Border _idleWeatherIcon = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _idleWeatherText = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Dim, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    readonly StackPanel _weatherLine = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), IsVisible = false };
    readonly Border _weatherIconHost = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _weatherTemp = new() { FontFamily = DisplayFont, FontSize = 26, FontWeight = FontWeight.Light, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _weatherText = new() { FontSize = 11, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 230, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
    readonly TextBlock _rainText = new() { FontSize = 11, Foreground = RainBlue, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 230, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
    readonly TextBlock _weatherAdd = new() { Text = "+ Weather", FontSize = 11, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 6, 0, 0), Cursor = new Cursor(StandardCursorType.Hand), IsVisible = false };
    readonly StackPanel _weatherSetup = new() { Margin = new Thickness(0, 10, 0, 0), IsVisible = false };
    readonly TextBox _weatherBox = NotchTextBox();
    readonly TextBlock _weatherPlaceholder = Placeholder("Your city, for the weather");
    readonly TextBlock _weatherHint = new() { Text = "Type a city and press Enter", FontSize = 11, Foreground = Dim, Margin = new Thickness(4, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>Builds the weather's parts and puts them beside the idle clock and the open notch's clock.</summary>
    void BuildWeather(Grid clockHeader)
    {
        _idleWeather.Children.Add(_idleWeatherIcon);
        _idleWeather.Children.Add(_idleWeatherText);
        _idleView.Children.Add(_idleWeather);

        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        top.Children.Add(_weatherIconHost);
        top.Children.Add(_weatherTemp);
        _weatherLine.Children.Add(top);
        _weatherLine.Children.Add(_weatherText);
        _weatherLine.Children.Add(_rainText);
        ToolTip.SetTip(_weatherLine, "Change the place");
        ToolTip.SetTip(_weatherAdd, "Show the weather for your city");
        Grid.SetColumn(_weatherLine, 1);
        Grid.SetColumn(_weatherAdd, 1);
        clockHeader.Children.Add(_weatherLine);
        clockHeader.Children.Add(_weatherAdd);
        _weatherLine.PointerReleased += (_, _) => { ChoosePlace(); BeginTyping(_weatherBox, untouched: true); };
        _weatherAdd.PointerReleased += (_, _) => { ChoosePlace(); BeginTyping(_weatherBox, untouched: true); };

        _weatherBox.MaxLength = 80;
        _weatherBox.TextChanged += (_, _) => _weatherPlaceholder.IsVisible = string.IsNullOrEmpty(_weatherBox.Text);
        _weatherBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _ = SearchPlaceAsync();
        }, RoutingStrategies.Tunnel);
        AllowTyping(_weatherBox);
        var field = new Grid();
        field.Children.Add(_weatherBox);
        field.Children.Add(_weatherPlaceholder);
        _weatherSetup.Children.Add(field);
        _weatherSetup.Children.Add(_weatherHint);
        _clockPanel.Children.Add(_weatherSetup);

        _weatherTick.Tick += (_, _) => _ = RefreshWeatherAsync();
    }

    void StartWeather()
    {
        _weatherTick.Stop();
        ShowWeather();
        if (!_settings.Weather || _settings.WeatherLatitude == null || _settings.WeatherLongitude == null) return;
        _weatherTick.Start();
        _ = RefreshWeatherAsync();
    }

    /// <summary>Asks Open-Meteo, unless it was asked less than 30 minutes ago (for this place).</summary>
    async Task RefreshWeatherAsync()
    {
#if DEBUG
        // HOLM_DEV_WEATHER="18.4,61,1": a temperature, a WMO code and day (1) or night (0), without asking anyone.
        if (Environment.GetEnvironmentVariable("HOLM_DEV_WEATHER") is { Length: > 0 } dev && dev.Split(',') is { Length: 3 } parts)
        {
            _weather = new WeatherNow(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1]), parts[2] == "1", 20, 0.4);
            _weatherAt = DateTime.UtcNow;
            ShowWeather();
            return;
        }
#endif
        if (!_settings.Weather || _fetchingWeather || _settings.WeatherLatitude is not { } lat || _settings.WeatherLongitude is not { } lon) return;
        if (!Weather.Due(_weatherAt, DateTime.UtcNow)) return;
        _fetchingWeather = true;
        ShowWeather();
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var now = await Weather.NowAsync(lat, lon, cancel.Token);
            if (_settings.WeatherLatitude != lat || _settings.WeatherLongitude != lon) return;
            _weatherAt = DateTime.UtcNow;
            if (now != null) _weather = now;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Diag.Log("Weather: " + ex.GetType().Name);
            _weatherAt = DateTime.UtcNow - Weather.Every + TimeSpan.FromMinutes(5); // try again in five minutes, not at once
        }
        finally { _fetchingWeather = false; }
        // Hours old is no weather to show.
        if (_weatherAt is { } at && _weather != null && DateTime.UtcNow - at > TimeSpan.FromHours(3)) _weather = null;
        ShowWeather();
    }

    void ShowWeather()
    {
        var weather = _settings.Weather ? _weather : null;
        _idleWeather.IsVisible = weather != null;
        _idleClock.IsVisible = weather == null; // the weather takes the clock's place
        _weatherLine.IsVisible = weather != null && !_choosingPlace;
        _weatherSetup.IsVisible = _settings.Weather && _choosingPlace;
        _weatherAdd.IsVisible = _settings.Weather && !_choosingPlace && weather == null;
        _weatherAdd.Text = _settings.WeatherLatitude == null ? "+ Weather"
            : _fetchingWeather ? "Loading weather…" : "Weather unavailable · will retry"; // a place is saved: the fetch failed, not the city
        if (weather != null)
        {
            var kind = weather.RainNote != null ? WeatherKind.Rain : weather.Kind;
            _idleWeatherIcon.Child = WeatherIcons.Make(kind, weather.IsDay, 12, Dim);
            _idleWeatherText.Text = weather.Degrees;
            _weatherIconHost.Child = WeatherIcons.Make(weather.Kind, weather.IsDay, 24, Brushes.White);
            _weatherTemp.Text = weather.Degrees;
            _weatherText.Text = $"{weather.Summary} · {(_settings.WeatherPlace ?? "").Split(',')[0]}"; // the city is enough here
            _rainText.Text = weather.RainNote ?? "";
            _rainText.IsVisible = weather.RainNote != null;
        }
        RefreshMediaWeather();
        if (_state == NotchState.Expanded) ApplyState(animate: true);
    }

    void ChoosePlace()
    {
        _placeSearch?.Cancel(); // a search from an earlier visit must not save a place later
        _placeSearch = null;
        ShowTool(Tool.None);
        _choosingPlace = true;
        _weatherBox.Text = "";
        _weatherHint.Text = _settings.WeatherPlace is { } place ? "Now: " + place : "Type a city and press Enter";
        ShowWeather();
    }

    async Task SearchPlaceAsync()
    {
        string name = (_weatherBox.Text ?? "").Trim();
        if (name.Length == 0 || _placeSearch != null) return; // one search at a time
        _weatherHint.Text = "Looking for " + name + "…";
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _placeSearch = cancel;
        try
        {
            var place = await Weather.FindAsync(name, cancel.Token);
            if (_placeSearch != cancel || !_choosingPlace) return; // Esc or closing meanwhile: save nothing
            if (place == null) { _weatherHint.Text = $"No place called “{name}” – check the spelling or try a larger town nearby"; return; }
            _settings.WeatherPlace = place.Label;
            _settings.WeatherLatitude = place.Latitude;
            _settings.WeatherLongitude = place.Longitude;
            _settings.Save();
            _choosingPlace = false;
            _weather = null;
            _weatherAt = null; // a new place is asked about at once
            EndTyping();
            StartWeather();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Diag.Log("Weather place: " + ex.GetType().Name);
            if (_choosingPlace && _placeSearch == cancel) _weatherHint.Text = "Can't reach the weather service – try again";
        }
        finally { if (_placeSearch == cancel) _placeSearch = null; }
    }

    /// <summary>Leaving the place field without a new city keeps the old one.</summary>
    void EndChoosingPlace()
    {
        if (!_choosingPlace) return;
        _placeSearch?.Cancel();
        _placeSearch = null;
        _choosingPlace = false;
        ShowWeather();
    }

    /// <summary>Customize › Weather: on or off, and the place.</summary>
    void FillWeatherMenu(MenuItem menu)
    {
        menu.Items.Clear();
        var on = new MenuItem { Header = "Show the weather", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.Weather };
        on.Click += (_, _) =>
        {
            _settings.Weather = !_settings.Weather;
            _settings.Save();
            StartWeather();
        };
        menu.Items.Add(on);
        var place = new MenuItem { Header = _settings.WeatherPlace is { } name ? $"Place: {name}…" : "Choose the place…", IsEnabled = _settings.Weather };
        place.Click += (_, _) =>
        {
            SetExpanded(true);
            ChoosePlace();
            BeginTyping(_weatherBox, untouched: true);
        };
        menu.Items.Add(place);
        menu.Items.Add(new MenuItem { Header = "From Open-Meteo, every 30 minutes", IsEnabled = false });
    }
}
