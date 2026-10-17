namespace Dst.Core.Features.WeatherForecasts;

[Alias("Dst.Core.Features.WeatherForecasts.IWeatherForecastGrain")]
public interface IWeatherForecastGrain : IGrainWithIntegerKey
{
    [Alias("GetAsync")]
    [Id(0)]
    ValueTask<WeatherForecast> GetAsync();
}
