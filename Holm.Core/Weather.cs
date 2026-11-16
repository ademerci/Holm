using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch;

/// <summary>What the weather symbol shows.</summary>
internal enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunder }

/// <summary>A place found by its name: what to call it and where it is.</summary>
internal sealed record WeatherPlace(string Name, string Label, double Latitude, double Longitude);

/// <summary>The weather now, and rain in the next hour if any.</summary>
internal sealed record WeatherNow(double Temperature, int Code, bool IsDay, int? RainInMinutes, double RainMm)
{
    public WeatherKind Kind => Weather.KindOf(Code);
    public string Summary => Weather.Describe(Code, IsDay);
    public string Degrees => Weather.Degrees(Temperature);
    /// <summary>"Rain in 20 min · 0.6 mm", "Rain now", or null when the next hour stays dry.</summary>
    public string? RainNote => RainInMinutes switch
    {
        null => null,
        <= 0 => $"Rain now · {Weather.Mm(RainMm)} in the next hour",
        _ => $"Rain in {RainInMinutes} min · {Weather.Mm(RainMm)}",
    };
}

/// <summary>
/// The weather from Open-Meteo (no key, no account): the place by name through its geocoding, then temperature,
/// sky and the next hour's rain. Holm asks at most every 30 minutes; only the place's name and coordinates go out.
/// </summary>
internal static class Weather
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static Weather() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Holm (+https://github.com/ademerci/Holm)");

    public static string GeocodeUrl(string name)
        => "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(name.Trim());

    public static string ForecastUrl(double latitude, double longitude)
        => "https://api.open-meteo.com/v1/forecast"
           + string.Create(CultureInfo.InvariantCulture, $"?latitude={latitude:0.####}&longitude={longitude:0.####}")
           + "&current=temperature_2m,weather_code,is_day&minutely_15=precipitation&forecast_minutely_15=6&timezone=GMT";

    public static async Task<WeatherPlace?> FindAsync(string name, CancellationToken cancel)
        => ParsePlace(await Http.GetStringAsync(GeocodeUrl(name), cancel));

    public static async Task<WeatherNow?> NowAsync(double latitude, double longitude, CancellationToken cancel)
        => ParseForecast(await Http.GetStringAsync(ForecastUrl(latitude, longitude), cancel), DateTime.UtcNow);

    /// <summary>The first match of a geocoding answer, or null when the name matched nothing.</summary>
    public static WeatherPlace? ParsePlace(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            return null;
        var first = results[0];
        string name = first.GetProperty("name").GetString() ?? "";
        string label = name;
        foreach (var part in new[] { "admin1", "country" })
            if (first.TryGetProperty(part, out var value) && value.GetString() is { Length: > 0 } text && text != name && !label.EndsWith(text, StringComparison.Ordinal))
                label += ", " + text;
        return new WeatherPlace(name, label, first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble());
    }

    /// <summary>The current weather and the next hour's rain from a forecast answer (times in GMT).</summary>
    public static WeatherNow? ParseForecast(string json, DateTime nowUtc)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("current", out var current)) return null;
        double temperature = current.GetProperty("temperature_2m").GetDouble();
        int code = current.GetProperty("weather_code").GetInt32();
        bool day = !current.TryGetProperty("is_day", out var isDay) || isDay.GetInt32() == 1;

        int? rainIn = null;
        double rainMm = 0;
        if (doc.RootElement.TryGetProperty("minutely_15", out var quarters)
            && quarters.TryGetProperty("time", out var times) && quarters.TryGetProperty("precipitation", out var amounts))
        {
            for (int i = 0; i < times.GetArrayLength() && i < amounts.GetArrayLength(); i++)
            {
                if (amounts[i].ValueKind != JsonValueKind.Number) continue;
                var start = DateTime.SpecifyKind(DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture), DateTimeKind.Utc);
                // The quarter we are in and the next hour.
                if (start + TimeSpan.FromMinutes(15) <= nowUtc || start >= nowUtc + TimeSpan.FromHours(1)) continue;
                double mm = amounts[i].GetDouble();
                if (mm < 0.1) continue;
                rainMm += mm;
                rainIn ??= Math.Max(0, (int)Math.Round((start - nowUtc).TotalMinutes));
            }
        }
        return new WeatherNow(temperature, code, day, rainIn, rainMm);
    }

    /// <summary>The WMO weather code as a symbol.</summary>
    public static WeatherKind KindOf(int code) => code switch
    {
        0 => WeatherKind.Clear,
        1 or 2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        >= 61 and <= 67 or >= 80 and <= 82 => WeatherKind.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherKind.Snow,
        >= 95 and <= 99 => WeatherKind.Thunder,
        _ => WeatherKind.Cloudy,
    };

    public static string Describe(int code, bool day) => code switch
    {
        0 => day ? "Sunny" : "Clear",
        1 => day ? "Mostly sunny" : "Mostly clear",
        2 => "Partly cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        61 or 80 => "Light rain",
        63 or 81 => "Rain",
        65 or 82 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 or 85 => "Light snow",
        73 or 75 or 77 or 86 => "Snow",
        >= 95 and <= 99 => "Thunderstorm",
        _ => "Cloudy",
    };

    /// <summary>"12°", "-3°", "0°" (never "-0°").</summary>
    public static string Degrees(double celsius)
    {
        int rounded = (int)Math.Round(celsius, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString(CultureInfo.InvariantCulture) + "°";
    }

    public static string Mm(double mm) => mm.ToString(mm < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " mm";

    /// <summary>Whether it's time to ask again: never sooner than every 30 minutes.</summary>
    public static bool Due(DateTime? lastUtc, DateTime nowUtc) => lastUtc is not { } last || nowUtc - last >= Every || nowUtc < last;
}
