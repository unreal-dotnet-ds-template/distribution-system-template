namespace Dst.Features.WeatherForecasts;

public static class WeatherForecastHelper
{
    private static string[] _summaries = [
        "Freezing",
        "Bracing",
        "Chilly",
        "Cool",
        "Mild",
        "Warm",
        "Balmy",
        "Hot",
        "Sweltering",
        "Scorching"
    ];

    private static Dictionary<int, int> _avgTemperatureC = new() {
        { 1, -14 },
        { 2, -3 },
        { 3, 14 },
        { 4, 20 },
        { 5, 25 },
        { 6, 25 },
        { 7, 29 },
        { 8, 35 },
        { 9, 24 },
        { 10, 18 },
        { 11, 7 },
        { 12, -2 }
    };

    public static CityWeatherGrainState Generate(DateOnly date, string location)
    {
        var res = new CityWeatherGrainState()
        {
            IsInitialized = true,
            Date = date,
            Location = location,
            TemperatureC = Random.Shared.Next(_avgTemperatureC[date.Month] - 4, _avgTemperatureC[date.Month] + 4),
            Summary = _summaries[Random.Shared.Next(_summaries.Length)]
        };
        return res;
    }
}
