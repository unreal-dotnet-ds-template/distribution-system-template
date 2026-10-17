using Dst.Core.Features.WeatherForecasts;

namespace Dst.Features.WeatherForecasts;

public static class WeatherForecastHelper
{
    public static WeatherForecastGrainState Generate()
    {
        string[] summaries = [
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

        var forecast = Enumerable.Range(1, 5)
            .Select(index => new WeatherForecastGrainState()
            {
                IsInitialized = true,
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = summaries[Random.Shared.Next(summaries.Length)]
            }).ToArray();

        return forecast.First();
    }
}
