using Dst.Core.Features.WeatherForecasts;

namespace Dst.Features.WeatherForecasts;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "<Pending>")]
public class WeatherForecastGrain : Grain, IWeatherForecastGrain
{
    private static WeatherForecast[] Generate()
    {
        string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];
#pragma warning disable CA5394 // Random is insecure — acceptable for sample weather data
        var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
#pragma warning restore CA5394
        return forecast;
    }

    private WeatherForecast[]? _data;

    public ValueTask<WeatherForecast[]> GetWeatherForecastsAsync()
    {
        if (_data == null)
        {
            _data = Generate();
        }

        return new ValueTask<WeatherForecast[]>(_data);
    }
}
