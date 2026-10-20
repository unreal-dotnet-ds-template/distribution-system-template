using System.ComponentModel;
using Dst.Core.Helpers;

namespace Dst.Core.Features.WeatherForecasts;

[Alias("Dst.Core.Features.WeatherForecasts.IWeatherForecastViewGrain")]
public interface IWeatherForecastViewGrain : IGrainWithIntegerKey
{
    [ReadOnly(true)]
    [Alias("GetAsync")]
    [Id(0)]
    ValueTask<GrainResult<GroupedWeatherForecastResult>> GetAsync();

    [ReadOnly(true)]
    [Alias("GetForWeekAsync")]
    [Id(1)]
    ValueTask<GrainResult<GroupedWeatherForecastResult>> GetForWeekAsync(
        [Id(0)]DateOnly dateFrom);

    [ReadOnly(true)]
    [Alias("GetForMonthAsync")]
    [Id(2)]
    ValueTask<GrainResult<GroupedWeatherForecastResult>> GetForMonthAsync(
        [Id(0)] int month);

    [Alias("SetValue")]
    [Id(3)]
    ValueTask<GrainResult<bool>> SetValue(
        [Id(0)] string location,
        [Id(1)] uint day,
        [Id(2)] int temperatureC);

    [ReadOnly(true)]
    [Alias("GetLocations")]
    [Id(4)]
    ValueTask<GrainResult<string[]>> GetLocations();
}

[GenerateSerializer, Immutable, Alias("Dst.Core.Features.WeatherForecasts.GroupedWeatherForecastResult")]
public sealed record GroupedWeatherForecastResult(
    [Id(0)] Dictionary<string, WeatherForecastResult[]> Locations);

[GenerateSerializer, Immutable, Alias("Dst.Core.Features.WeatherForecasts.WeatherForecastResult")]
public sealed record WeatherForecastResult(
    [Id(0)] DateOnly Date,
    [Id(1)] int TemperatureC);
