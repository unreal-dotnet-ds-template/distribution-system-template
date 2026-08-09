namespace Mdr.Orleans.Core.Features.WeatherForecasts;

[GenerateSerializer, Immutable]
public sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
