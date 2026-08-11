using Orleans;

namespace Dst.Core.Features.WeatherForecasts;

[Alias("IWeatherForecastGrain")]
public interface IWeatherForecastGrain : IGrainWithIntegerKey
{
    [Alias("GetWeatherForecastsAsync")]
    [Id(0)]
    ValueTask<WeatherForecast[]> GetWeatherForecastsAsync();
}
