namespace Dst.Core.Features.WeatherForecasts;

[GenerateSerializer, Immutable, Alias("Dst.Core.Features.WeatherForecasts.WeatherForecast")]
public sealed record WeatherForecast(
    [Id(0)] DateOnly Date,
    [Id(1)] int TemperatureC,
    [Id(2)] string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
