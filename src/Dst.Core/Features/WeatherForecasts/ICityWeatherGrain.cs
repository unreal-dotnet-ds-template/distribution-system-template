using Dst.Core.Helpers;

namespace Dst.Core.Features.WeatherForecasts;

[Alias("Dst.Core.Features.WeatherForecasts.ICityWeatherGrain")]
public interface ICityWeatherGrain : IGrainWithIntegerCompoundKey
{
    [Alias("GetAsync")]
    [Id(0)]
    ValueTask<GrainResult<CityWeatherResult>> GetAsync();

    [Alias("UpdateAsync")]
    [Id(1)]
    ValueTask<GrainResult<bool>> UpdateAsync();
}

[GenerateSerializer, Immutable, Alias("Dst.Core.Features.WeatherForecasts.CityWeatherResult")]
public sealed record CityWeatherResult(
    [Id(0)] DateOnly Date,
    [Id(1)] string Location,
    [Id(2)] int TemperatureC,
    [Id(3)] string? Summary);
