using Orleans;

namespace Dst.Orleans.Core.Features.WeatherForecasts;

public interface IWeatherForecastGrain : IGrainWithIntegerKey
{
    Task<WeatherForecast[]> GetWeatherForecastsAsync();
}
