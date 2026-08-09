using Orleans;

namespace Mdr.Orleans.Core.Features.WeatherForecasts;

public interface IWeatherForecastGrain : IGrainWithIntegerKey
{
    Task<WeatherForecast[]> GetWeatherForecastsAsync();
}
