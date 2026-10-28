using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;

namespace WinNotch.Tests;

public class WeatherTests
{
    const string Geocoded = """
        {"results":[{"id":2867714,"name":"Munich","latitude":48.13743,"longitude":11.57549,"elevation":524.0,"feature_code":"PPLA",
        "country_code":"DE","admin1_id":2951839,"timezone":"Europe/Berlin","population":1260391,"country_id":2921044,
        "country":"Germany","admin1":"Bavaria"}],"generationtime_ms":0.9}
        """;

    [Fact]
    public void Reads_the_first_place_found()
    {
        var place = Weather.ParsePlace(Geocoded)!;
        Assert.Equal("Munich", place.Name);
        Assert.Equal("Munich, Bavaria, Germany", place.Label);
        Assert.Equal(48.13743, place.Latitude, 5);
        Assert.Equal(11.57549, place.Longitude, 5);
    }

    [Fact]
    public void A_city_state_is_named_once()
    {
        var place = Weather.ParsePlace("""{"results":[{"name":"Berlin","latitude":52.52,"longitude":13.41,"country":"Germany","admin1":"Berlin"}]}""")!;
        Assert.Equal("Berlin, Germany", place.Label);
    }

    [Theory]
    [InlineData("""{"generationtime_ms":0.3}""")]
    [InlineData("""{"results":[]}""")]
    public void Nothing_found_is_null(string json) => Assert.Null(Weather.ParsePlace(json));

    // Written with ' for readability.
    static string Forecast(string precipitation) => (
        "{'latitude':48.14,'longitude':11.58,'utc_offset_seconds':0,'timezone':'GMT'," +
        "'current_units':{'time':'iso8601','interval':'seconds','temperature_2m':'°C','weather_code':'wmo code','is_day':''}," +
        "'current':{'time':'2026-10-27T12:00','interval':900,'temperature_2m':12.4,'weather_code':2,'is_day':1}," +
        "'minutely_15_units':{'time':'iso8601','precipitation':'mm'}," +
        "'minutely_15':{'time':['2026-10-27T12:00','2026-10-27T12:15','2026-10-27T12:30','2026-10-27T12:45','2026-10-27T13:00','2026-10-27T13:15']," +
        "'precipitation':" + precipitation + "}}").Replace('\'', '"');

    static readonly DateTime Now = new(2026, 10, 27, 12, 5, 0, DateTimeKind.Utc);

    [Fact]
    public void Reads_the_weather_now()
    {
        var now = Weather.ParseForecast(Forecast("[0,0,0,0,0,0]"), Now)!;
        Assert.Equal(12.4, now.Temperature);
        Assert.Equal(WeatherKind.PartlyCloudy, now.Kind);
        Assert.Equal("Partly cloudy", now.Summary);
        Assert.Equal("12°", now.Degrees);
        Assert.Null(now.RainInMinutes);
        Assert.Null(now.RainNote);
    }

    [Fact]
    public void Finds_rain_in_the_next_hour()
    {
        var now = Weather.ParseForecast(Forecast("[0,0.05,0.4,0.3,0,2.0]"), Now)!;
        Assert.Equal(25, now.RainInMinutes);                // 12:30 is 25 minutes from 12:05
        Assert.Equal(0.7, now.RainMm, 3);                   // 13:15 is beyond the hour, 0.05 is too little
        Assert.Equal("Rain in 25 min · 0.7 mm", now.RainNote);
    }

    [Fact]
    public void Rain_already_falling_is_now()
    {
        var now = Weather.ParseForecast(Forecast("[0.6,0.2,0,0,0,0]"), Now)!;
        Assert.Equal(0, now.RainInMinutes);
        Assert.Equal("Rain now · 0.8 mm in the next hour", now.RainNote);
    }

    [Fact]
    public void Missing_values_are_skipped()
    {
        var now = Weather.ParseForecast(Forecast("[null,null,1.2,null,null,null]"), Now)!;
        Assert.Equal(25, now.RainInMinutes);
    }

    [Fact]
    public void Night_and_no_minutely_data()
    {
        var json = """{"current":{"time":"2026-10-27T23:00","temperature_2m":-0.4,"weather_code":0,"is_day":0}}""";
        var now = Weather.ParseForecast(json, Now)!;
        Assert.Equal("Clear", now.Summary);
        Assert.Equal("0°", now.Degrees);
        Assert.Null(now.RainNote);
    }

    [Fact]
    public void No_current_weather_is_null() => Assert.Null(Weather.ParseForecast("""{"error":true,"reason":"Latitude must be in range"}""", Now));

    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(1, "PartlyCloudy")]
    [InlineData(3, "Cloudy")]
    [InlineData(45, "Fog")]
    [InlineData(53, "Drizzle")]
    [InlineData(63, "Rain")]
    [InlineData(81, "Rain")]
    [InlineData(75, "Snow")]
    [InlineData(86, "Snow")]
    [InlineData(95, "Thunder")]
    [InlineData(42, "Cloudy")]   // unknown
    public void Weather_codes_become_symbols(int code, string kind) => Assert.Equal(kind, Weather.KindOf(code).ToString());

    [Theory]
    [InlineData(12.5, "13°")]
    [InlineData(-2.5, "-3°")]
    [InlineData(-0.4, "0°")]
    [InlineData(0.4, "0°")]
    public void Degrees_are_whole(double celsius, string expected) => Assert.Equal(expected, Weather.Degrees(celsius));

    [Fact]
    public void Urls_use_a_point_whatever_the_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string url = Weather.ForecastUrl(48.13743, 11.57549);
            Assert.Contains("latitude=48.1374&longitude=11.5755", url);
            Assert.Contains("timezone=GMT", url);
        }
        finally { CultureInfo.CurrentCulture = before; }
        Assert.EndsWith("name=S%C3%A3o%20Paulo", Weather.GeocodeUrl(" São Paulo "));
    }

    [Fact]
    public void Asks_at_most_every_thirty_minutes()
    {
        var t = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(Weather.Due(null, t));
        Assert.False(Weather.Due(t, t.AddMinutes(29)));
        Assert.True(Weather.Due(t, t.AddMinutes(30)));
        Assert.True(Weather.Due(t, t.AddMinutes(-5))); // the clock went back
    }

    [Fact]
    public void Settings_from_before_the_weather_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Connectivity\":false}")!;
        Assert.False(old.Connectivity);
        Assert.True(old.Weather);
        Assert.Null(old.WeatherPlace);
        Assert.True(old.WeatherAutoLocation);
        Assert.True(old.MediaControls);
    }

    [Theory]
    [InlineData(47.3769, 8.5417, 47.4, 8.5)]
    [InlineData(-33.8688, 151.2093, -33.9, 151.2)]
    public void Automatic_location_is_rounded_before_storage_or_forecasts(double lat, double lon, double expectedLat, double expectedLon)
    {
        var place = WeatherLocation.Approximate(lat, lon)!;
        Assert.Equal(expectedLat, place.Latitude);
        Assert.Equal(expectedLon, place.Longitude);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void Invalid_automatic_locations_are_rejected(double lat, double lon)
        => Assert.Null(WeatherLocation.Approximate(lat, lon));

    [Fact]
    public void Media_controls_setting_persists_and_old_settings_default_on()
    {
        Assert.True(JsonSerializer.Deserialize<AppSettings>("{}")!.MediaControls);
        var saved = JsonSerializer.Serialize(new AppSettings { MediaControls = false, WeatherAutoLocation = false });
        var loaded = JsonSerializer.Deserialize<AppSettings>(saved)!;
        Assert.False(loaded.MediaControls);
        Assert.False(loaded.WeatherAutoLocation);
    }
}
