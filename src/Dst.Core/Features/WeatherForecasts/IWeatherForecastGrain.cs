using Orleans;

namespace Dst.Core.Features.WeatherForecasts;

[Alias("IWeatherForecastGrain")]
public interface IWeatherForecastGrain : IGrainWithIntegerKey
{
    Task<WeatherForecast[]> GetWeatherForecastsAsync();
}
