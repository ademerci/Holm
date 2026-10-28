using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The weather where you say you are: the temperature and a symbol in the idle notch, the sky and the next hour's
/// rain in the open notch. From Open-Meteo, at most every 30 minutes; use a saved city or approximate Windows location.
/// </summary>
public partial class MainWindow
{
    WeatherNow? _weather;
    DateTime? _weatherAt;
    DispatcherTimer _weatherTick = null!;
    bool _fetchingWeather, _choosingPlace;
    bool _locatingWeather, _locationAttempted, _weatherClosed;
    CancellationTokenSource? _locationCancel;
    Func<CancellationToken, Task<WeatherPlace?>> _locateWeather = WeatherLocation.FindAsync;
    Func<double, double, CancellationToken, Task<WeatherNow?>> _fetchWeather = Weather.NowAsync;

    void InitWeather()
    {
        _weatherTick = MakeTimer((int)Weather.Every.TotalMilliseconds, () => _ = RefreshWeatherAsync());
        AllowTyping(WeatherBox);
    }

    void StartWeather()
    {
        _weatherTick.Stop();
        ShowWeather();
        if (!_settings.Weather) { _locationCancel?.Cancel(); return; }
        if (_settings.WeatherLatitude == null || _settings.WeatherLongitude == null)
        {
            _ = LocateWeatherAsync();
            return;
        }
        _weatherTick.Start();
        _ = RefreshWeatherAsync();
    }

    async Task LocateWeatherAsync()
    {
        if (_weatherClosed || !_settings.Weather || !_settings.WeatherAutoLocation || _locatingWeather || _locationAttempted
            || _choosingPlace || (_settings.WeatherLatitude != null && _settings.WeatherLongitude != null)) return;
        _locationAttempted = true;
        _locatingWeather = true;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _locationCancel = cancel;
        ShowWeather();
        try
        {
            var place = await _locateWeather(cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            if (place == null || _weatherClosed || !_settings.Weather || !_settings.WeatherAutoLocation || _choosingPlace) return;
            _settings.WeatherPlace = place.Label;
            _settings.WeatherLatitude = place.Latitude;
            _settings.WeatherLongitude = place.Longitude;
            _settings.Save();
            StartWeather();
        }
        catch (Exception ex)
        {
            // Windows location can be disabled, denied or unavailable. Manual city selection remains available.
            Diag.Log("Approximate weather location: " + ex.GetType().Name);
        }
        finally
        {
            _locationCancel = null;
            _locatingWeather = false;
            if (!_weatherClosed) ShowWeather();
        }
    }

    void MenuWeatherAutoLocation_Click(object sender, RoutedEventArgs e)
    {
        _settings.WeatherAutoLocation = MenuWeatherAutoLocation.IsChecked;
        _settings.Save();
        if (!_settings.WeatherAutoLocation) _locationCancel?.Cancel();
        else { _locationAttempted = false; StartWeather(); }
    }

    /// <summary>Asks Open-Meteo, unless it was asked less than 30 minutes ago (for this place).</summary>
    async Task RefreshWeatherAsync()
    {
        if (!_settings.Weather || _fetchingWeather || _settings.WeatherLatitude is not { } lat || _settings.WeatherLongitude is not { } lon) return;
        if (!Weather.Due(_weatherAt, DateTime.UtcNow)) return;
        _fetchingWeather = true;
        ShowWeather();
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var now = await _fetchWeather(lat, lon, cancel.Token);
            if (_settings.WeatherLatitude != lat || _settings.WeatherLongitude != lon || _weatherClosed) return;
            _weatherAt = DateTime.UtcNow;
            if (now != null) _weather = now;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException or FormatException)
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
        IdleWeather.Visibility = weather != null ? Visibility.Visible : Visibility.Collapsed;
        WeatherLine.Visibility = weather != null && !_choosingPlace ? Visibility.Visible : Visibility.Collapsed;
        WeatherSetup.Visibility = _settings.Weather && _choosingPlace ? Visibility.Visible : Visibility.Collapsed;
        WeatherAdd.Visibility = _settings.Weather && !_choosingPlace && weather == null ? Visibility.Visible : Visibility.Collapsed;
        WeatherAdd.Text = _locatingWeather ? "Finding approximate location…"
            : _settings.WeatherLatitude == null ? (_locationAttempted ? "Choose weather city" : "+ Weather")
            : _fetchingWeather ? "Loading weather…" : "Weather unavailable · choose city";
        if (weather != null)
        {
            IdleWeatherIcon.Child = WeatherIcons.Make(weather.Kind, weather.IsDay, 12, DimBrush);
            IdleWeatherText.Text = weather.Degrees;
            WeatherIconHost.Child = WeatherIcons.Make(weather.Kind, weather.IsDay, 24, System.Windows.Media.Brushes.White);
            WeatherTemp.Text = weather.Degrees;
            WeatherText.Text = $"{weather.Summary} · {(_settings.WeatherPlace ?? "").Split(',')[0]}"; // the city is enough here
            RainText.Text = weather.RainNote ?? "";
            RainText.Visibility = weather.RainNote != null ? Visibility.Visible : Visibility.Collapsed;
            if (weather.RainNote != null) IdleWeatherIcon.Child = WeatherIcons.Make(WeatherKind.Rain, weather.IsDay, 12, DimBrush);
        }
        ShowIdle(); // the weather takes the clock's place
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    // ───────────── choosing the place ─────────────

    void WeatherLine_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ChoosePlace();
    }

    void ChoosePlace()
    {
        _locationCancel?.Cancel();
        ShowTool(Tool.None);
        _choosingPlace = true;
        RefreshMediaUi();
        WeatherBox.Text = "";
        WeatherHint.Text = _settings.WeatherPlace is { } place ? "Now: " + place : "Type a city and press Enter";
        ShowWeather();
    }

    void WeatherBox_TextChanged(object sender, TextChangedEventArgs e)
        => WeatherPlaceholder.Visibility = WeatherBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    async void WeatherBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        string name = WeatherBox.Text.Trim();
        if (name.Length == 0) return;
        WeatherHint.Text = "Looking for " + name + "…";
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var place = await Weather.FindAsync(name, cancel.Token);
            if (place == null) { WeatherHint.Text = $"No place called “{name}”"; return; }
            _settings.WeatherPlace = place.Label;
            _settings.WeatherLatitude = place.Latitude;
            _settings.WeatherLongitude = place.Longitude;
            _settings.Save();
            _choosingPlace = false;
            RefreshMediaUi();
            _weather = null;
            _weatherAt = null; // a new place is asked about at once
            EndTyping();
            StartWeather();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException or FormatException)
        {
            Diag.Log("Weather place: " + ex.GetType().Name);
            WeatherHint.Text = "Can't reach the weather service – try again";
        }
    }

    void FillWeatherMenu()
    {
        MenuWeather.Items.Clear();
        var on = new MenuItem { Header = "Show the weather", IsCheckable = true, IsChecked = _settings.Weather };
        on.Click += (_, _) =>
        {
            _settings.Weather = on.IsChecked;
            _settings.Save();
            StartWeather();
        };
        MenuWeather.Items.Add(on);
        var place = new MenuItem { Header = _settings.WeatherPlace is { } name ? $"Place: {name}…" : "Choose the place…", IsEnabled = _settings.Weather };
        place.Click += (_, _) =>
        {
            SetExpanded(true);
            ChoosePlace();
        };
        MenuWeather.Items.Add(place);
        var automatic = new MenuItem
        {
            Header = "Use approximate Windows location", IsEnabled = _settings.Weather && !_locatingWeather,
            ToolTip = "Uses Windows location services once. Rounded coordinates go to Open-Meteo. No IP lookup service."
        };
        automatic.Click += async (_, _) =>
        {
            _settings.WeatherAutoLocation = true;
            _settings.WeatherPlace = null;
            _settings.WeatherLatitude = _settings.WeatherLongitude = null;
            _weather = null;
            _weatherAt = null;
            _settings.Save();
            _locationAttempted = false;
            _choosingPlace = false;
            RefreshMediaUi();
            await LocateWeatherAsync();
        };
        MenuWeather.Items.Add(automatic);
        MenuWeather.Items.Add(new MenuItem { Header = "From Open-Meteo, every 30 minutes", IsEnabled = false });
    }
}
