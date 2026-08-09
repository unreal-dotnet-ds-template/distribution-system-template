using Orleans;

namespace Dst.Core.Features.WeatherForecasts;

public interface IWeatherForecastGrain : IGrainWithIntegerKey
{
    Task<WeatherForecast[]> GetWeatherForecastsAsync();
}
